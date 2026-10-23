--[[
Cloud90 Game Link for Hades (PLAN-HADES.md).

Live-stream events change the run: Cloud90 Hades Link (the companion app)
writes the effects it receives to Cloud90Inbox.lua; this mod reads it a few
times a second with loadfile (the game's Lua has no io), applies each effect
once, and answers on the game's console:

  CLOUD90 hello <version>
  CLOUD90 inbox <path>            where it found the inbox
  CLOUD90 state <0|1>             1 = Zagreus can take effects now
  CLOUD90 ack <id> ok
  CLOUD90 ack <id> <reason>       the same Spanish reasons as Nebula's link

Effects only run in a chamber of the Underworld (Tartarus, Asphodel,
Elysium, Styx), with Zagreus alive and no screen open. Timed ones end when
they run out, when he leaves the chamber, or on panic. Nothing but `death`
kills him; with "Modo sin morir" (safe) the last Death Defiance is kept too.
]]

Cloud90 = Cloud90 or {}
local C = Cloud90

C.Version = "1.1.0"
C.Seen = C.Seen or {}          -- effect id -> true
C.SeenOrder = C.SeenOrder or {}
C.Active = C.Active or {}      -- timed slot -> { name, untilT, stop }
C.ShieldUntil = C.ShieldUntil or 0
C.GraceUntil = C.GraceUntil or 0
C.Paused = C.Paused or false
C.Generation = C.Generation or 0

local TICK = 0.2
local GRACE = 5
local MAX_TIMED = 60
local SEEN_KEEP = 200
local NA = "no disponible ahora"

C.InboxPaths = {
	"Cloud90Inbox.lua",
	"Scripts/Cloud90Inbox.lua",
	"Content/Scripts/Cloud90Inbox.lua",
	"../Content/Scripts/Cloud90Inbox.lua",
	"..\\Content\\Scripts\\Cloud90Inbox.lua",
}

local function say( text )
	print( "CLOUD90 " .. text )
end

local function now()
	return _worldTime or 0
end

local function hero()
	return CurrentRun ~= nil and CurrentRun.Hero or nil
end

local BIOMES = { Tartarus = 1, Asphodel = 2, Elysium = 3, Styx = 4 }

local function biome()
	local room = CurrentRun ~= nil and CurrentRun.CurrentRoom or nil
	return room ~= nil and BIOMES[room.RoomSetName] or nil
end

-- Zagreus can take effects now.
local function inPlay()
	local h = hero()
	if h == nil or h.IsDead or h.ObjectId == nil or CurrentRun.CurrentRoom == nil then
		return false
	end
	if ActiveScreens ~= nil and next( ActiveScreens ) ~= nil then
		return false
	end
	return biome() ~= nil
end

local function pick( list )
	if #list == 0 then
		return nil
	end
	return list[ math.random( #list ) ]
end

-- ─────────────────────────── timed effects ───────────────────────────

-- One effect per slot: a repeat extends it, another in the same slot
-- (slow vs. super speed…) ends the first and takes over.
local function timed( slot, name, seconds, start, stop )
	seconds = math.min( math.max( seconds or 10, 1 ), MAX_TIMED )
	local current = C.Active[slot]
	if current ~= nil and current.name == name then
		current.untilT = math.min( math.max( current.untilT, now() ) + seconds, now() + MAX_TIMED )
		return
	end
	if current ~= nil then
		C.Active[slot] = nil
		pcall( current.stop )
	end
	start()
	C.Active[slot] = { name = name, untilT = now() + seconds, stop = stop }
end

local function endTimed( keepShield )
	for slot, t in pairs( C.Active ) do
		if not ( keepShield and slot == "shield" ) then
			C.Active[slot] = nil
			pcall( t.stop )
		end
	end
end

local function tickTimed()
	for slot, t in pairs( C.Active ) do
		if now() >= t.untilT then
			C.Active[slot] = nil
			pcall( t.stop )
		end
	end
end

local function seconds( e )
	return ( e.ms or 10000 ) / 1000
end

-- ─────────────────────────── the effects ───────────────────────────

local GODS = {
	zeus = "ZeusUpgrade", poseidon = "PoseidonUpgrade", athena = "AthenaUpgrade",
	ares = "AresUpgrade", aphrodite = "AphroditeUpgrade", artemis = "ArtemisUpgrade",
	dionysus = "DionysusUpgrade", hermes = "HermesUpgrade", demeter = "DemeterUpgrade",
}

local CURSES = {
	"ChaosCurseDeathWeaponTrait", "ChaosCurseNoMoneyTrait", "ChaosCurseAmmoUseDelayTrait",
	"ChaosCurseHiddenRoomReward", "ChaosCurseDamageTrait", "ChaosCurseTrapDamageTrait",
	"ChaosCurseHealthTrait", "ChaosCurseDashRangeTrait", "ChaosCurseMoveSpeedTrait",
	"ChaosCurseSpawnTrait", "ChaosCursePrimaryAttackTrait", "ChaosCurseSecondaryAttackTrait",
	"ChaosCurseCastAttackTrait", "ChaosCurseDashAttackTrait",
}

local CONSUMABLES = { "HealDropMinor", "RoomRewardMaxHealthDrop", "RoomRewardMoneyDrop", "LockKeyDrop", "GiftDrop" }

local function spawnLoot( lootName )
	local h = hero()
	if LootData == nil or LootData[lootName] == nil then
		return false, NA
	end
	CreateLoot({ Name = lootName, SpawnPoint = h.ObjectId, OffsetX = 150, OffsetY = 0 })
	return true
end

local function dropConsumable( name )
	GiveRandomConsumables({ LootOptions = { { Name = name, Chance = 1 } }, Range = 120 })
	return true
end

local function colorGrade( name )
	return function( e )
		timed( "grade", name, seconds( e ),
			function() AdjustColorGrading({ Name = name, Duration = 0.4 }) end,
			function() AdjustColorGrading({ Name = "Off", Duration = 0.4 }) end )
		return true
	end
end

local function zoom( fraction )
	return function( e )
		timed( "zoom", tostring( fraction ), seconds( e ),
			function() AdjustZoom({ Fraction = fraction, LerpTime = 0.4 }) end,
			function()
				local room = CurrentRun ~= nil and CurrentRun.CurrentRoom or nil
				AdjustZoom({ Fraction = ( room ~= nil and room.ZoomFraction ) or 0.9, LerpTime = 0.4 })
			end )
		return true
	end
end

local function heroSpeed( factor, name )
	return function( e )
		local id = hero().ObjectId
		if GetUnitDataValue({ Id = id, Property = "Speed" }) == nil then
			return false, NA
		end
		-- Read in start(), after the effect it replaces put the speed back.
		local base
		timed( "speed", name, seconds( e ),
			function()
				base = GetUnitDataValue({ Id = id, Property = "Speed" })
				SetUnitProperty({ Property = "Speed", Value = base * factor, DestinationId = id })
			end,
			function() SetUnitProperty({ Property = "Speed", Value = base, DestinationId = id }) end )
		return true
	end
end

local function heroScale( fraction, name )
	return function( e )
		local id = hero().ObjectId
		timed( "scale", name, seconds( e ),
			function() SetScale({ Id = id, Fraction = fraction }) end,
			function() SetScale({ Id = id, Fraction = 1.0 }) end )
		return true
	end
end

local function gameSpeed( fraction, name )
	return function( e )
		timed( "sim", name, seconds( e ),
			function() AdjustSimulationSpeed({ Fraction = fraction, LerpTime = 0.3 }) end,
			function() AdjustSimulationSpeed({ Fraction = 1.0, LerpTime = 0.3 }) end )
		return true
	end
end

local function noControl( slot, names )
	return function( e )
		timed( slot, slot, seconds( e ),
			function() ToggleControl({ Names = names, Enabled = false }) end,
			function() ToggleControl({ Names = names, Enabled = true }) end )
		return true
	end
end

-- Enemies of the zone he's in: the game loads each zone's art when he gets
-- there, so another zone's would be invisible (or worse).
local function zoneEnemies( elite )
	local b = biome()
	local names = {}
	local set = EnemySets ~= nil and b ~= nil and EnemySets[ "EnemiesBiome" .. b ] or nil
	if set == nil or EnemyData == nil then
		return names
	end
	for _, name in ipairs( set ) do
		local candidate = elite and ( name .. "Elite" ) or name
		local data = EnemyData[candidate]
		if data ~= nil and not data.IsBoss then
			table.insert( names, candidate )
		end
	end
	return names
end

-- The streamer's pick (e.arg, an enemy's base name: "HeavyMelee"), if it's
-- one of this zone's; else nil, and one of the zone's at random comes.
local function chosenEnemy( arg, elite )
	if type( arg ) ~= "string" or arg == "" then
		return nil
	end
	local b = biome()
	local set = EnemySets ~= nil and b ~= nil and EnemySets[ "EnemiesBiome" .. b ] or nil
	if set == nil or EnemyData == nil then
		return nil
	end
	local candidate = elite and ( arg .. "Elite" ) or arg
	for _, name in ipairs( set ) do
		if name == arg or name == arg .. "Elite" then
			return EnemyData[candidate] ~= nil and candidate or nil
		end
	end
	return nil
end

local function spawnEnemies( count, elite )
	return function( e )
		local names = zoneEnemies( elite )
		local chosen = chosenEnemy( e.arg, elite )
		if chosen ~= nil then
			names = { chosen }
		end
		if #names == 0 then
			return false, NA
		end
		local h = hero()
		for i = 1, count do
			local name = pick( names )
			local data = EnemyData[name]
			local enemy = DeepCopyTable( data )
			local angle = ( i / count ) * 2 * math.pi
			enemy.ObjectId = SpawnUnit({
				Name = data.Name or name, Group = "Standing", DestinationId = h.ObjectId,
				OffsetX = math.cos( angle ) * 350, OffsetY = math.sin( angle ) * 200,
			})
			SetupEnemyObject( enemy, CurrentRun, { SkipSpawnVoiceLines = true } )
		end
		return true
	end
end

-- troll: a shield refuses it, "Modo troll" off never sends it.
-- grace: also waits GRACE seconds after a new chamber or a teleport.
local EFFECTS = {
	-- Controls
	paralyze = { troll = true, run = function( e )
		timed( "freeze", "paralyze", math.min( seconds( e ), 10 ),
			function() FreezePlayerUnit( "Cloud90" ) end,
			function() UnfreezePlayerUnit( "Cloud90" ) end )
		return true
	end },
	no_dash = { troll = true, run = noControl( "rush", { "Rush" } ) },
	no_attack = { troll = true, run = noControl( "attack", { "Attack1", "Attack2" } ) },
	no_cast = { troll = true, run = noControl( "cast", { "Attack3" } ) },
	slow = { troll = true, run = heroSpeed( 0.5, "slow" ) },
	super_speed = { run = heroSpeed( 1.8, "super_speed" ) },

	-- Screen
	grayscale = { troll = true, run = colorGrade( "Sepia" ) },
	frozen_screen = { troll = true, run = colorGrade( "Frozen" ) },
	poison_screen = { troll = true, run = colorGrade( "Poison" ) },
	invert_colors = { troll = true, run = colorGrade( "ChaosInversion" ) },
	blackout = { troll = true, run = colorGrade( "Dusk" ) },
	blur = { troll = true, run = function( e )
		timed( "blur", "blur", seconds( e ),
			function()
				AdjustRadialBlurDistance({ Fraction = 0.25, Duration = 0 })
				AdjustRadialBlurStrength({ Fraction = 1.5, Duration = 0.3 })
			end,
			function() AdjustRadialBlurStrength({ Fraction = 0, Duration = 0.3 }) end )
		return true
	end },
	zoom_in = { troll = true, run = zoom( 1.5 ) },
	zoom_out = { troll = true, run = zoom( 0.55 ) },
	earthquake = { troll = true, run = function( e )
		timed( "quake", "earthquake", seconds( e ), function() end, function() end )
		return true
	end },

	-- Zagreus
	heal = { run = function( e )
		local h = hero()
		if h.Health == nil or h.Health >= h.MaxHealth then
			return false, NA
		end
		Heal( h, { HealAmount = 25, SourceName = "Cloud90" } )
		return true
	end },
	damage = { troll = true, grace = true, run = function( e )
		local h = hero()
		if h.Health == nil or h.Health <= 1 then
			return false, NA
		end
		Damage( h, { triggeredById = h.ObjectId, DamageAmount = 20, MinHealth = 1, PureDamage = true } )
		return true
	end },
	gold_add = { run = function( e )
		AddMoney( 50, "Cloud90" )
		return true
	end },
	gold_remove = { troll = true, run = function( e )
		local money = CurrentRun.Money or 0
		if money <= 0 then
			return false, NA
		end
		SpendMoney( math.min( 50, money ), "Cloud90" )
		return true
	end },
	death_defiance = { run = function( e ) return dropConsumable( "LastStandDrop" ) end },
	remove_death_defiance = { troll = true, grace = true, run = function( e )
		local h = hero()
		local count = h.LastStands ~= nil and #h.LastStands or 0
		if count == 0 then
			return false, NA
		end
		if e.safe and count <= 1 then
			return false, "modo sin morir"
		end
		table.remove( h.LastStands )
		if UpdateLifePips ~= nil then
			UpdateLifePips( h )
		end
		return true
	end },
	invulnerable = { run = function( e )
		timed( "invulnerable", "invulnerable", seconds( e ),
			function() SetPlayerInvulnerable( "Cloud90" ) end,
			function() SetPlayerVulnerable( "Cloud90" ) end )
		return true
	end },
	giant = { run = heroScale( 1.6, "giant" ) },
	mini = { troll = true, run = heroScale( 0.55, "mini" ) },
	slow_motion = { troll = true, run = gameSpeed( 0.5, "slow_motion" ) },
	turbo = { troll = true, run = gameSpeed( 1.5, "turbo" ) },
	death = { troll = true, grace = true, run = function( e )
		if e.safe then
			return false, "modo sin morir"
		end
		local h = hero()
		Damage( h, { triggeredById = h.ObjectId, DamageAmount = ( h.MaxHealth or 100 ) + 9999, PureDamage = true } )
		return true
	end },

	-- Boons and items
	boon_random = { run = function( e )
		local gods = {}
		for _, lootName in pairs( GODS ) do
			if LootData ~= nil and LootData[lootName] ~= nil then
				table.insert( gods, lootName )
			end
		end
		local lootName = pick( gods )
		if lootName == nil then
			return false, NA
		end
		return spawnLoot( lootName )
	end },
	hammer = { run = function( e ) return spawnLoot( "WeaponUpgrade" ) end },
	chaos_boon = { run = function( e ) return spawnLoot( "TrialUpgrade" ) end },
	consumable = { run = function( e )
		local names = {}
		for _, name in ipairs( CONSUMABLES ) do
			if ( ObstacleData ~= nil and ObstacleData[name] ~= nil ) or ( ConsumableData ~= nil and ConsumableData[name] ~= nil ) then
				table.insert( names, name )
			end
		end
		local name = pick( names )
		if name == nil then
			return false, NA
		end
		return dropConsumable( name )
	end },

	-- Heavy troll
	chaos_curse = { troll = true, run = function( e )
		local names = {}
		for _, name in ipairs( CURSES ) do
			if TraitData ~= nil and TraitData[name] ~= nil and not HeroHasTrait( name ) then
				table.insert( names, name )
			end
		end
		local name = pick( names )
		if name == nil then
			return false, NA
		end
		AddTraitToHero({ TraitName = name })
		return true
	end },
	lose_boon = { troll = true, run = function( e )
		local h = hero()
		local boons = {}
		for _, trait in pairs( h.Traits or {} ) do
			if trait.Name ~= nil and IsGodTrait( trait.Name ) then
				table.insert( boons, trait )
			end
		end
		local trait = pick( boons )
		if trait == nil then
			return false, NA
		end
		RemoveTraitData( h, trait )
		return true
	end },
	back_to_start = { troll = true, grace = true, run = function( e )
		local h = hero()
		if C.StartPoint == nil then
			return false, NA
		end
		Teleport({ Id = h.ObjectId, DestinationId = C.StartPoint })
		C.GraceUntil = now() + GRACE
		return true
	end },

	-- Enemies
	spawn_enemies = { troll = true, run = spawnEnemies( 3, false ) },
	spawn_elite = { troll = true, run = spawnEnemies( 1, true ) },
	spawn_horde = { troll = true, run = spawnEnemies( 6, false ) },

	-- Shared with Mario Galaxy
	shield = { run = function( e )
		timed( "shield", "shield", seconds( e ),
			function() C.ShieldUntil = math.huge end,
			function() C.ShieldUntil = 0 end )
		return true
	end },
}

for god, lootName in pairs( GODS ) do
	EFFECTS[ "boon_" .. god ] = { run = function( e ) return spawnLoot( lootName ) end }
end

-- ─────────────────────────── the inbox ───────────────────────────

local function readInbox()
	local paths = C.InboxPath ~= nil and { C.InboxPath } or C.InboxPaths
	for _, path in ipairs( paths ) do
		local chunk = loadfile( path )
		if chunk ~= nil then
			if C.InboxPath == nil then
				C.InboxPath = path
				say( "inbox " .. path )
			end
			local ok, data = pcall( chunk )
			if ok and type( data ) == "table" then
				return data
			end
			return nil
		end
	end
	if not C.SaidNoInbox then
		C.SaidNoInbox = true
		say( "noinbox" )
	end
	return nil
end

local function remember( id )
	C.Seen[id] = true
	table.insert( C.SeenOrder, id )
	while #C.SeenOrder > SEEN_KEEP do
		C.Seen[ table.remove( C.SeenOrder, 1 ) ] = nil
	end
end

local function apply( e )
	local def = EFFECTS[ e.effect ]
	if def == nil then
		return false, "efecto desconocido"
	end
	if C.Paused then
		return false, "en pausa"
	end
	if def.troll and now() < C.ShieldUntil then
		return false, "escudo activo"
	end
	if not inPlay() then
		return false, NA
	end
	if def.grace and now() < C.GraceUntil then
		return false, NA
	end
	local ok, result, reason = pcall( def.run, e )
	if not ok then
		say( "error " .. tostring( e.effect ) .. " " .. tostring( result ) )
		return false, NA
	end
	return result, reason
end

-- A new chamber: what was running ends there, the harshest effects wait a
-- moment, and where Zagreus stands is the chamber's start.
local function onNewChamber()
	endTimed( true )
	C.GraceUntil = now() + GRACE
	C.StartPoint = nil
	local h = hero()
	if h ~= nil and h.ObjectId ~= nil then
		local ok, id = pcall( SpawnObstacle, { Name = "BlankObstacle", DestinationId = h.ObjectId, Group = "Standing" } )
		if ok then
			C.StartPoint = id
		end
	end
end

local function tick()
	local room = CurrentRun ~= nil and CurrentRun.CurrentRoom or nil
	if room ~= C.Room then
		C.Room = room
		if room ~= nil then
			onNewChamber()
		end
	end
	local h = hero()
	if h == nil or h.IsDead then
		endTimed( true )
	end
	tickTimed()

	-- The earthquake is a series of short shakes.
	if C.Active.quake ~= nil and ( C.LastShake == nil or now() - C.LastShake >= 0.3 ) then
		C.LastShake = now()
		ShakeScreen({ Speed = 500, Distance = 6, FalloffSpeed = 1000, Duration = 0.3 })
	end

	local playing = inPlay()
	if playing ~= C.WasInPlay then
		C.WasInPlay = playing
		say( "state " .. ( playing and "1" or "0" ) )
	end

	local inbox = readInbox()
	if inbox == nil then
		return
	end
	local control = inbox.control or {}
	if control.paused and not C.Paused then
		C.Paused = true
		endTimed( true )
	elseif not control.paused then
		C.Paused = false
	end
	for _, e in ipairs( inbox.effects or {} ) do
		if e.id ~= nil and not C.Seen[ e.id ] then
			remember( e.id )
			local ok, reason = apply( e )
			say( "ack " .. e.id .. " " .. ( ok and "ok" or ( reason or NA ) ) )
		end
	end
end

local function loop( generation )
	while C.Generation == generation do
		local ok, err = pcall( tick )
		if not ok then
			say( "error tick " .. tostring( err ) )
		end
		wait( TICK )
	end
end

-- Each map load starts a fresh loop; the one before stops.
OnAnyLoad{ function( triggerArgs )
	C.Generation = C.Generation + 1
	thread( loop, C.Generation )
end }

say( "hello " .. C.Version )
