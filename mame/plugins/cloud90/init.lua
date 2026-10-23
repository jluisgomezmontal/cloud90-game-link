--[[
Cloud90 Game Link for MAME (PLAN-KOF2002.md, PLAN-MSLUG3.md). Cloud90 Game
Link (the app) writes the effects it receives to <dir>/inbox.lua; this plugin
reads it a few times a second, applies each effect once and answers in
<dir>/acks.txt:

  hello <version> <set>
  state <0|1>            1 = the player is playing and the effects can run
  ack <id> ok
  ack <id> <reason>      the same Spanish reasons as the other links

<dir> is CLOUD90_DIR (the app sets it, one per game), else plugins/cloud90.
The King of Fighters 2002 and Metal Slug 3; another machine answers "no
disponible ahora". Effects only run while playing a game the player started
(not the title demo). Timed ones are undone when they end, when play stops,
and on panic. Nothing kills the player outright.
]]
local exports = {
	name = "cloud90",
	version = "1.1.0",
	description = "Cloud90 Game Link",
	license = "MIT",
	author = { name = "Cloud90" },
}

local NA = "no disponible ahora"
local MAX_TIMED = 60
local GRACE = 3          -- s after a KOF round starts: no damage / KO yet
local READ_EVERY = 10    -- frames between inbox reads

local START = { ":edge:joy:START", "1 Player Start" }

-- KOF 2002 (MAME set kof2002), checked in phase 0 with MAME 0.289.
local KOF2002 = {
	kind = "kof",
	hp = { 0x108239, 0x108439 },     -- signed, 102 = full
	stocks = { 0x1082E3, 0x1084E3 },
	timer = 0x10A7D2,                 -- BCD, 0x60 at the start of a round
	minTimer = 1,
	maxHp = 102,
	joy = ":edge:joy:JOY1",
	start = START,
	blocks = { no_jump = { "Up" }, no_punch = { "A", "C" }, no_kick = { "B", "D" } },
	pulses = { auto_jump = { keys = { "Up" }, every = 40, len = 4 } },
	mash = { "A", "B", "C", "D" },
}

-- Metal Slug 3 (MAME set mslug3), checked in phase 0 with MAME 0.289.
local MSLUG3 = {
	kind = "mslug",
	mode = 0x1002BE,                  -- 2 while playing (3 continue, 0/1 title)
	lives = 0x1002BB,                 -- spare lives ("1UP=n"), the HUD redraws on a death
	grenades = 0x10EAF5,
	timer = 0x10ED78,                 -- BCD; 0 does not kill
	minTimer = 5,
	invul = 0x100581,                 -- respawn blink, counts down from ~120
	weapon = 0x10EAF3,                -- 0 = pistol
	ammo = 0x10EAFA,                  -- u16, 0xFFFF = infinite (pistol)
	joy = ":edge:joy:JOY1",
	start = START,
	blocks = { no_jump = { "B" }, no_shoot = { "A" }, no_grenade = { "C" } },
	pulses = {
		auto_jump = { keys = { "B" }, every = 50, len = 4 },
		auto_fire = { keys = { "A" }, every = 8, len = 3 },
	},
	-- weapon ids from phase 0 screenshots, ammo as the game hands it out
	weapons = {
		hmg = { 1, 200 }, shotgun = { 2, 30 }, rocket = { 3, 30 }, flame = { 4, 30 },
		laser = { 6, 200 }, chaser = { 12, 30 }, drop = { 14, 30 },
	},
	weaponOrder = { "hmg", "shotgun", "rocket", "flame", "laser", "chaser", "drop" },
	-- The task table: 112 slots of 0xB0 bytes; a node is slot+0x0C (node+0
	-- routine, +4 next, +8 prev, +0xC parent). Free nodes are a stack the
	-- game's allocator (ROM 0x5814) pops: a clone must come from there too.
	tasks = {
		base = 0x100700, size = 0xB0, count = 112, root = 0x10044C,
		freeHead = 0x10540C, freeCount = 0x105410, idle = 0x567A,
		enemyTag = 0x10C120,          -- slot+0x44 of an enemy (its hit list)
	},
	maxClones = 6,
	keepFree = 16,
}

local GAMES = { kof2002 = KOF2002, mslug3 = MSLUG3 }

local CONTROLS = { "Up", "Down", "Left", "Right", "A", "B", "C", "D" }

function exports.startplugin()
	local S = {
		seen = {}, seenOrder = {}, active = {}, frame = 0, clones = {},
		shieldUntil = 0, graceUntil = 0, paused = false,
	}
	exports.state = S
	exports.subscriptions = exports.subscriptions or {}
	local dir = os.getenv( "CLOUD90_DIR" ) or "plugins/cloud90"
	local game, mem, screen, fields, EFFECTS

	local function now() return S.frame / 60 end

	local function say( text )
		local f = io.open( dir .. "/acks.txt", "a" )
		if f then f:write( text .. "\n" ); f:close() end
		print( "CLOUD90 " .. text )
	end

	-- ── the machine ──
	local function signed( v ) return v >= 128 and v - 256 or v end
	local function hp( p ) return signed( mem:read_u8( game.hp[p] ) ) end
	local function setHp( p, v ) mem:write_u8( game.hp[p], math.max( 1, math.min( game.maxHp, v ) ) & 0xFF ) end
	local function stocks( p ) return mem:read_u8( game.stocks[p] ) end
	local function fromBcd( v ) return ( v >> 4 ) * 10 + ( v & 15 ) end
	local function toBcd( n ) return ( ( n // 10 ) << 4 ) | ( n % 10 ) end
	local function timer() return fromBcd( mem:read_u8( game.timer ) ) end
	local function setTimer( n ) mem:write_u8( game.timer, toBcd( math.max( game.minTimer, math.min( 99, n ) ) ) ) end

	local function inPlay()
		if not game or not S.started then return false end
		if game.kind == "mslug" then return mem:read_u8( game.mode ) == 2 end
		local a, b = hp( 1 ), hp( 2 )
		return a >= 1 and a <= game.maxHp and b >= 1 and b <= game.maxHp
	end

	-- The player's real input for a control (keyboard or pad, as mapped).
	local function real( name )
		local f = fields[name]
		return f ~= nil and manager.machine.input:seq_pressed( f:input_seq( "standard" ) )
	end

	-- ── timed effects: one per slot; a repeat extends, another takes over ──
	local function timed( slot, name, seconds, start, stop )
		seconds = math.min( math.max( seconds or 10, 1 ), MAX_TIMED )
		local current = S.active[slot]
		if current and current.name == name then
			current.untilT = math.min( math.max( current.untilT, now() ) + seconds, now() + MAX_TIMED )
			return
		end
		if current then S.active[slot] = nil; pcall( current.stop ) end
		if start then start() end
		S.active[slot] = { name = name, untilT = now() + seconds, stop = stop or function() end }
	end
	local function endTimed( keepShield )
		for slot, t in pairs( S.active ) do
			if not ( keepShield and slot == "shield" ) then S.active[slot] = nil; pcall( t.stop ) end
		end
	end
	local function on( slot, name ) local t = S.active[slot]; return t ~= nil and ( name == nil or t.name == name ) end
	local function secondsOf( e ) return ( e.ms or 10000 ) / 1000 end

	-- ── effects every game has ──
	local function control( name ) return function( e ) timed( "control:" .. name, name, secondsOf( e ) ); return true end end
	local function screenFx( name ) return function( e ) timed( "screen", name, secondsOf( e ) ); return true end end
	local function speed( rate, name )
		return function( e )
			timed( "speed", name, secondsOf( e ),
				function() manager.machine.video.throttle_rate = rate end,
				function() manager.machine.video.throttle_rate = 1.0 end )
			return true
		end
	end
	local function timerEffects( list )
		list.timer_add = { run = function() setTimer( timer() + 20 ); return true end }
		list.timer_remove = { troll = true, run = function()
			if timer() <= game.minTimer then return false, NA end
			setTimer( timer() - 20 )
			return true
		end }
		list.timer_freeze = { run = function( e )
			local value
			timed( "timer", "timer_freeze", secondsOf( e ), function() value = mem:read_u8( game.timer ) end )
			S.active.timer.value = S.active.timer.value or value
			return true
		end }
	end
	-- troll: refused by a shield, never sent with "Modo troll" off.
	-- grace: also waits GRACE s after a round starts.
	local function commonEffects()
		local list = {
			invert_controls = { troll = true, run = control( "invert" ) },
			paralyze = { troll = true, run = function( e ) timed( "control:paralyze", "paralyze", math.min( secondsOf( e ), 10 ) ); return true end },
			blackout = { troll = true, run = screenFx( "blackout" ) },
			tunnel_vision = { troll = true, run = screenFx( "tunnel" ) },
			slow_motion = { troll = true, run = speed( 0.5, "slow_motion" ) },
			turbo = { troll = true, run = speed( 1.5, "turbo" ) },
			shield = { run = function( e )
				timed( "shield", "shield", secondsOf( e ), function() S.shieldUntil = math.huge end, function() S.shieldUntil = 0 end )
				return true
			end },
		}
		for name in pairs( game.blocks ) do list[name] = { troll = true, run = control( name ) } end
		for name in pairs( game.pulses ) do list[name] = { troll = true, run = control( name ) } end
		if game.mash then list.button_mash = { troll = true, run = control( "button_mash" ) } end
		timerEffects( list )
		return list
	end

	-- ── KOF 2002 ──
	local function kofEffects()
		local list = commonEffects()
		local function hold( p, name )
			return function( e )
				local value
				timed( "hold" .. p, name, secondsOf( e ), function() value = hp( p ) end )
				S.active[ "hold" .. p ].value = S.active[ "hold" .. p ].value or value
				return true
			end
		end
		local function heal( p )
			return function()
				if hp( p ) >= game.maxHp then return false, NA end
				setHp( p, hp( p ) + 25 )
				return true
			end
		end
		local function fullPower( p )
			return function()
				if stocks( p ) >= 3 then return false, NA end
				mem:write_u8( game.stocks[p], 3 )
				return true
			end
		end
		list.heal = { run = heal( 1 ) }
		list.damage = { troll = true, grace = true, run = function()
			if hp( 1 ) <= 1 then return false, NA end
			setHp( 1, hp( 1 ) - 20 )
			return true
		end }
		list.power_full = { run = fullPower( 1 ) }
		list.power_empty = { troll = true, run = function()
			if stocks( 1 ) == 0 then return false, NA end
			mem:write_u8( game.stocks[1], 0 )
			return true
		end }
		list.invincible = { run = hold( 1, "invincible" ) }
		-- "A un golpe del KO": the game only KOs on a hit, so health 1.
		list.ko = { troll = true, grace = true, run = function( e )
			if e.safe then return false, "modo sin morir" end
			if hp( 1 ) <= 1 then return false, NA end
			setHp( 1, 1 )
			return true
		end }
		list.rival_heal = { troll = true, run = heal( 2 ) }
		list.rival_power = { troll = true, run = fullPower( 2 ) }
		list.rival_invincible = { troll = true, run = hold( 2, "rival_invincible" ) }
		return list
	end

	-- ── Metal Slug 3 ──
	local T
	local function slot( k ) return T.base + k * T.size end
	local function isEnemy( s )
		return mem:read_u32( s + 0x0C ) ~= T.idle and mem:read_u32( s + 0x44 ) == T.enemyTag
			and mem:read_u32( s + 0x18 ) == T.root
	end
	local function liveClones()
		local n, keep = 0, {}
		for _, c in ipairs( S.clones ) do
			if mem:read_u32( c.node ) ~= T.idle and now() - c.t < MAX_TIMED then n = n + 1; keep[#keep + 1] = c end
		end
		S.clones = keep
		return n
	end
	-- A copy of enemy slot s in a node popped from the game's free stack,
	-- linked after it in the task list, a little to one side.
	local function clone( s )
		if mem:read_u16( T.freeCount ) < game.keepFree then return false end
		local node = mem:read_u32( T.freeHead )
		if node < T.base or node >= T.base + T.count * T.size then return false end
		mem:write_u32( T.freeHead, mem:read_u32( node + 8 ) )
		mem:write_u16( T.freeCount, mem:read_u16( T.freeCount ) - 1 )
		local d = node - 0x0C
		for i = 0, T.size - 1 do
			if i < 0x10 or i >= 0x18 then mem:write_u8( d + i, mem:read_u8( s + i ) ) end
		end
		local src = s + 0x0C
		local nx = mem:read_u32( src + 4 )
		mem:write_u32( node + 4, nx )
		mem:write_u32( node + 8, src )
		mem:write_u32( nx + 8, node )
		mem:write_u32( src + 4, node )
		local dx = math.random( 12, 24 ) * ( math.random( 2 ) == 1 and -1 or 1 )
		for _, off in ipairs( { 0x34, 0x48, 0x50 } ) do mem:write_u16( d + off, ( mem:read_u16( s + off ) + dx ) & 0xFFFF ) end
		S.clones[#S.clones + 1] = { node = node, t = now() }
		return true
	end

	local function mslugEffects()
		local list = commonEffects()
		local function byte( addr ) return mem:read_u8( addr ) end
		list.grenades_add = { run = function()
			if byte( game.grenades ) >= 99 then return false, NA end
			mem:write_u8( game.grenades, math.min( 99, byte( game.grenades ) + 10 ) )
			return true
		end }
		list.grenades_remove = { troll = true, run = function()
			if byte( game.grenades ) == 0 then return false, NA end
			mem:write_u8( game.grenades, 0 )
			return true
		end }
		list.life_add = { run = function()
			if byte( game.lives ) >= 9 then return false, NA end
			mem:write_u8( game.lives, byte( game.lives ) + 1 )
			return true
		end }
		list.life_remove = { troll = true, run = function()
			if byte( game.lives ) == 0 or byte( game.lives ) > 9 then return false, NA end
			mem:write_u8( game.lives, byte( game.lives ) - 1 )
			return true
		end }
		list.invincible = { run = function( e ) timed( "invul", "invincible", secondsOf( e ) ); return true end }
		list.give_weapon = { run = function( e )
			local name = e.arg
			if game.weapons[name] == nil then name = game.weaponOrder[ math.random( #game.weaponOrder ) ] end
			local w = game.weapons[name]
			mem:write_u8( game.weapon, w[1] )
			mem:write_u16( game.ammo, w[2] )
			return true
		end }
		list.remove_weapon = { troll = true, run = function()
			if byte( game.weapon ) == 0 then return false, NA end
			mem:write_u8( game.weapon, 0 )
			mem:write_u16( game.ammo, 0xFFFF )
			return true
		end }
		list.ammo_add = { run = function()
			local ammo = mem:read_u16( game.ammo )
			if byte( game.weapon ) == 0 or ammo == 0xFFFF or ammo >= 999 then return false, NA end
			mem:write_u16( game.ammo, math.min( 999, ammo + 100 ) )
			return true
		end }
		list.spawn_enemies = { troll = true, run = function( e )
			local want = math.min( math.max( tonumber( e.arg ) or 1, 1 ), 3 )
			local room = game.maxClones - liveClones()
			if room <= 0 then return false, NA end
			local enemies = {}
			for k = 0, T.count - 1 do if isEnemy( slot( k ) ) then enemies[#enemies + 1] = slot( k ) end end
			if #enemies == 0 then return false, NA end
			local made = 0
			for _ = 1, math.min( want, room ) do
				if clone( enemies[ math.random( #enemies ) ] ) then made = made + 1 end
			end
			if made == 0 then return false, NA end
			return true
		end }
		return list
	end

	local function apply( e )
		local def = EFFECTS and EFFECTS[ e.effect ]
		if def == nil then return false, "efecto desconocido" end
		if S.paused then return false, "en pausa" end
		if def.troll and now() < S.shieldUntil then return false, "escudo activo" end
		if not inPlay() then return false, NA end
		if def.grace and now() < S.graceUntil then return false, NA end
		local ok, result, reason = pcall( def.run, e )
		if not ok then say( "error " .. tostring( e.effect ) .. " " .. tostring( result ) ); return false, NA end
		return result, reason
	end

	-- ── each frame: holds, controls, screen ──
	local function applyHolds()
		if game.kind == "kof" then
			for p = 1, 2 do
				local t = S.active[ "hold" .. p ]
				if t and t.value and hp( p ) < t.value then setHp( p, t.value ) end
			end
		elseif S.active.invul then
			mem:write_u8( game.invul, 120 )
		end
		local t = S.active.timer
		if t and t.value then mem:write_u8( game.timer, t.value ) end
	end

	local touched = false
	local function applyControls()
		local any = false
		for slotName in pairs( S.active ) do if slotName:sub( 1, 8 ) == "control:" then any = true end end
		if not any then
			if touched then
				for _, n in ipairs( CONTROLS ) do if fields[n] then fields[n]:clear_value() end end
				touched = false
			end
			return
		end
		local want = {}
		for _, n in ipairs( CONTROLS ) do want[n] = real( n ) end
		if on( "control:invert" ) then
			want.Left, want.Right = want.Right, want.Left
			want.Up, want.Down = want.Down, want.Up
		end
		for name, keys in pairs( game.blocks ) do
			if on( "control:" .. name ) then for _, n in ipairs( keys ) do want[n] = false end end
		end
		for name, p in pairs( game.pulses ) do
			if on( "control:" .. name ) then
				for _, n in ipairs( p.keys ) do want[n] = want[n] or ( S.frame % p.every ) < p.len end
			end
		end
		if game.mash and on( "control:button_mash" ) then
			for _, n in ipairs( game.mash ) do want[n] = want[n] or math.random( 6 ) == 1 end
		end
		if on( "control:paralyze" ) then for _, n in ipairs( CONTROLS ) do want[n] = false end end
		for _, n in ipairs( CONTROLS ) do if fields[n] then fields[n]:set_value( want[n] and 1 or 0 ) end end
		touched = true
	end

	local function drawScreen()
		if not screen then return end
		local w, h = screen.width, screen.height
		local t = S.active.screen
		if t and t.name == "blackout" then
			local hw, hh = w * 0.16, h * 0.2
			local cx, cy = w / 2, h / 2
			screen:draw_box( 0, 0, w, cy - hh, 0xff000000, 0xff000000 )
			screen:draw_box( 0, cy + hh, w, h, 0xff000000, 0xff000000 )
			screen:draw_box( 0, cy - hh, cx - hw, cy + hh, 0xff000000, 0xff000000 )
			screen:draw_box( cx + hw, cy - hh, w, cy + hh, 0xff000000, 0xff000000 )
		elseif t and t.name == "tunnel" then
			local bx, by = w * 0.22, h * 0.22
			screen:draw_box( 0, 0, w, by, 0xe8000000, 0xe8000000 )
			screen:draw_box( 0, h - by, w, h, 0xe8000000, 0xe8000000 )
			screen:draw_box( 0, by, bx, h - by, 0xe8000000, 0xe8000000 )
			screen:draw_box( w - bx, by, w, h - by, 0xe8000000, 0xe8000000 )
		end
		if S.active.shield then
			local left = math.max( 0, math.ceil( S.active.shield.untilT - now() ) )
			screen:draw_text( "center", 2, "ESCUDO " .. left .. " s", 0xff7dd3fc, 0x80000000 )
		end
	end

	-- ── the inbox ──
	local function remember( id )
		S.seen[id] = true
		table.insert( S.seenOrder, id )
		while #S.seenOrder > 200 do S.seen[ table.remove( S.seenOrder, 1 ) ] = nil end
	end

	local function readInbox()
		local chunk = loadfile( dir .. "/inbox.lua" )
		if not chunk then return end
		local ok, inbox = pcall( chunk )
		if not ok or type( inbox ) ~= "table" then return end
		local paused = inbox.control and inbox.control.paused
		if paused and not S.paused then endTimed( true ) end
		S.paused = paused and true or false
		for _, e in ipairs( inbox.effects or {} ) do
			if e.id and not S.seen[e.id] then
				remember( e.id )
				local ok2, reason = apply( e )
				say( "ack " .. e.id .. " " .. ( ok2 and "ok" or ( reason or NA ) ) )
			end
		end
	end

	-- ── the frame ──
	local wasInPlay = false
	local function frame()
		S.frame = S.frame + 1
		if not game then return end
		-- A game the player started (not the title demo): Start as the game
		-- sees it (keyboard, pad, or another tool).
		if not S.started and fields.start then
			local f = fields.start
			if ( f.port:read() & f.mask ) ~= ( f.defvalue & f.mask ) then S.started = true end
		end
		local playing = inPlay()
		if playing ~= wasInPlay then
			wasInPlay = playing
			if playing then S.graceUntil = now() + GRACE else endTimed( true ) end
			say( "state " .. ( playing and "1" or "0" ) )
		end
		for slotName, t in pairs( S.active ) do
			if now() >= t.untilT then S.active[slotName] = nil; pcall( t.stop ) end
		end
		if playing then applyHolds() end
		applyControls()
		if S.frame % READ_EVERY == 0 then readInbox() end
		drawScreen()
	end

	local function start()
		game = GAMES[ manager.machine.system.name ]
		local cpu = manager.machine.devices[":maincpu"]
		mem = cpu and cpu.spaces["program"]
		screen = manager.machine.screens[":screen"]
		fields = {}
		EFFECTS = nil
		if game then
			T = game.tasks
			EFFECTS = game.kind == "mslug" and mslugEffects() or kofEffects()
			local joy = manager.machine.ioport.ports[ game.joy ]
			for _, n in ipairs( CONTROLS ) do fields[n] = joy and joy.fields[ "P1 " .. n ] end
			local sp = manager.machine.ioport.ports[ game.start[1] ]
			fields.start = sp and sp.fields[ game.start[2] ]
		end
		say( "hello " .. exports.version .. " " .. manager.machine.system.name .. ( game and "" or " (no soportado)" ) )
	end

	local function guard( fn )
		return function()
			local ok, err = pcall( fn )
			if not ok and not S.reported then S.reported = true; say( "error " .. tostring( err ) ) end
		end
	end

	table.insert( exports.subscriptions, emu.add_machine_reset_notifier( guard( start ) ) )
	table.insert( exports.subscriptions, emu.add_machine_frame_notifier( guard( frame ) ) )
	table.insert( exports.subscriptions, emu.add_machine_stop_notifier( function()
		pcall( function() manager.machine.video.throttle_rate = 1.0 end )
	end ) )
end

return exports
