--[[
Cloud90 Game Link for MAME (PLAN-KOF2002.md). Cloud90 Game Link (the app)
writes the effects it receives to <dir>/inbox.lua; this plugin reads it a few
times a second, applies each effect once and answers in <dir>/acks.txt:

  hello <version>
  state <0|1>            1 = a round is on and the effects can run
  ack <id> ok
  ack <id> <reason>      the same Spanish reasons as the other links

<dir> is CLOUD90_DIR (the app sets it), else plugins/cloud90. Only The King
of Fighters 2002 for now; another machine answers "no disponible ahora".
Effects only run during a round of a game the player started (not the
title demo). Timed ones are undone when they end, between rounds, and on
panic. Nothing sets a health to 0 (the game only KOs on a hit).
]]
local exports = {
	name = "cloud90",
	version = "1.0.0",
	description = "Cloud90 Game Link",
	license = "MIT",
	author = { name = "Cloud90" },
}

local NA = "no disponible ahora"
local MAX_TIMED = 60
local GRACE = 3          -- s after a round starts: no damage / KO yet
local READ_EVERY = 10    -- frames between inbox reads

-- KOF 2002 (MAME set kof2002), checked in phase 0 with MAME 0.289.
local KOF2002 = {
	hp = { 0x108239, 0x108439 },     -- signed, 102 = full
	stocks = { 0x1082E3, 0x1084E3 },
	timer = 0x10A7D2,                 -- BCD, 0x60 at the start of a round
	maxHp = 102,
	joy = ":edge:joy:JOY1",
	start = { ":edge:joy:START", "1 Player Start" },
}
local GAMES = { kof2002 = KOF2002 }

local CONTROLS = { "Up", "Down", "Left", "Right", "A", "B", "C", "D" }

function exports.startplugin()
	local S = {
		seen = {}, seenOrder = {}, active = {}, frame = 0,
		shieldUntil = 0, graceUntil = 0, paused = false,
	}
	exports.state = S
	exports.subscriptions = exports.subscriptions or {}
	local dir = os.getenv( "CLOUD90_DIR" ) or "plugins/cloud90"
	local game, mem, screen, fields

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
	local function setTimer( n ) mem:write_u8( game.timer, toBcd( math.max( 1, math.min( 99, n ) ) ) ) end

	local function inRound()
		if not game or not S.started then return false end
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

	-- ── the effects ──
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

	-- troll: refused by a shield, never sent with "Modo troll" off.
	-- grace: also waits GRACE s after a round starts.
	local EFFECTS = {
		invert_controls = { troll = true, run = control( "invert" ) },
		no_jump = { troll = true, run = control( "no_jump" ) },
		no_punch = { troll = true, run = control( "no_punch" ) },
		no_kick = { troll = true, run = control( "no_kick" ) },
		paralyze = { troll = true, run = function( e ) timed( "control:paralyze", "paralyze", math.min( secondsOf( e ), 10 ) ); return true end },
		auto_jump = { troll = true, run = control( "auto_jump" ) },
		button_mash = { troll = true, run = control( "button_mash" ) },
		blackout = { troll = true, run = screenFx( "blackout" ) },
		tunnel_vision = { troll = true, run = screenFx( "tunnel" ) },
		slow_motion = { troll = true, run = speed( 0.5, "slow_motion" ) },
		turbo = { troll = true, run = speed( 1.5, "turbo" ) },
		heal = { run = heal( 1 ) },
		damage = { troll = true, grace = true, run = function()
			if hp( 1 ) <= 1 then return false, NA end
			setHp( 1, hp( 1 ) - 20 )
			return true
		end },
		power_full = { run = fullPower( 1 ) },
		power_empty = { troll = true, run = function()
			if stocks( 1 ) == 0 then return false, NA end
			mem:write_u8( game.stocks[1], 0 )
			return true
		end },
		timer_add = { run = function() setTimer( timer() + 20 ); return true end },
		timer_remove = { troll = true, run = function()
			if timer() <= 1 then return false, NA end
			setTimer( timer() - 20 )
			return true
		end },
		timer_freeze = { run = function( e )
			local value
			timed( "timer", "timer_freeze", secondsOf( e ), function() value = mem:read_u8( game.timer ) end )
			S.active.timer.value = S.active.timer.value or value
			return true
		end },
		invincible = { run = hold( 1, "invincible" ) },
		-- "A un golpe del KO": the game only KOs on a hit, so health 1.
		ko = { troll = true, grace = true, run = function( e )
			if e.safe then return false, "modo sin morir" end
			if hp( 1 ) <= 1 then return false, NA end
			setHp( 1, 1 )
			return true
		end },
		rival_heal = { troll = true, run = heal( 2 ) },
		rival_power = { troll = true, run = fullPower( 2 ) },
		rival_invincible = { troll = true, run = hold( 2, "rival_invincible" ) },
		shield = { run = function( e )
			timed( "shield", "shield", secondsOf( e ), function() S.shieldUntil = math.huge end, function() S.shieldUntil = 0 end )
			return true
		end },
	}

	local function apply( e )
		local def = EFFECTS[ e.effect ]
		if def == nil then return false, "efecto desconocido" end
		if S.paused then return false, "en pausa" end
		if def.troll and now() < S.shieldUntil then return false, "escudo activo" end
		if not inRound() then return false, NA end
		if def.grace and now() < S.graceUntil then return false, NA end
		local ok, result, reason = pcall( def.run, e )
		if not ok then say( "error " .. tostring( e.effect ) .. " " .. tostring( result ) ); return false, NA end
		return result, reason
	end

	-- ── each frame: holds, controls, screen ──
	local function applyHolds()
		for p = 1, 2 do
			local t = S.active[ "hold" .. p ]
			if t and t.value and hp( p ) < t.value then setHp( p, t.value ) end
		end
		local t = S.active.timer
		if t and t.value then mem:write_u8( game.timer, t.value ) end
	end

	local touched = false
	local function applyControls()
		local any = false
		for slot in pairs( S.active ) do if slot:sub( 1, 8 ) == "control:" then any = true end end
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
		if on( "control:no_jump" ) then want.Up = false end
		if on( "control:no_punch" ) then want.A, want.C = false, false end
		if on( "control:no_kick" ) then want.B, want.D = false, false end
		if on( "control:auto_jump" ) then want.Up = want.Up or ( S.frame % 40 ) < 4 end
		if on( "control:button_mash" ) then
			for _, n in ipairs( { "A", "B", "C", "D" } ) do want[n] = want[n] or math.random( 6 ) == 1 end
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
	local wasInRound = false
	local function frame()
		S.frame = S.frame + 1
		if not game then return end
		-- A game the player started (not the title demo): Start as the game
		-- sees it (keyboard, pad, or another tool).
		if not S.started and fields.start then
			local f = fields.start
			if ( f.port:read() & f.mask ) ~= ( f.defvalue & f.mask ) then S.started = true end
		end
		local playing = inRound()
		if playing ~= wasInRound then
			wasInRound = playing
			if playing then S.graceUntil = now() + GRACE else endTimed( true ) end
			say( "state " .. ( playing and "1" or "0" ) )
		end
		for slot, t in pairs( S.active ) do
			if now() >= t.untilT then S.active[slot] = nil; pcall( t.stop ) end
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
		if game then
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
