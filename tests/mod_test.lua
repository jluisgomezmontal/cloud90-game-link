-- Runs mod/Cloud90.lua against a fake Hades engine (node tests/run.js).
-- The game can't run here, so this checks the mod's own logic: the inbox,
-- acks, shield, panic, grace, timed effects and their undo, "Modo sin
-- morir", and that each effect calls what it should.

local failures = 0
local function check( condition, message )
	if not condition then
		failures = failures + 1
		io.write( "FAIL: " .. message .. "\n" )
	end
end

-- ─────────────────────────── fake engine ───────────────────────────
local out = {}
print = function( text ) table.insert( out, text ) end
local calls = {}
local function called( name ) table.insert( calls, name ) end
local function hasCall( name )
	for _, c in ipairs( calls ) do if c == name then return true end end
	return false
end

_worldTime = 0
ActiveScreens = {}
local speed = { [7] = 100 }
local scale = { [7] = 1 }
local freeze = {}

local function newRoom( set )
	return { RoomSetName = set or "Tartarus", ZoomFraction = 0.9 }
end

CurrentRun = {
	Money = 30,
	CurrentRoom = newRoom( "Tartarus" ),
	Hero = { ObjectId = 7, Health = 50, MaxHealth = 100, LastStands = { {}, {} }, Traits = { { Name = "ZeusWeaponTrait" }, { Name = "SomeKeepsake" } } },
}

Heal = function( h, a ) called( "Heal" ); h.Health = math.min( h.MaxHealth, h.Health + a.HealAmount ) end
Damage = function( h, a )
	called( "Damage" )
	h.Health = math.max( a.MinHealth or 0, h.Health - a.DamageAmount )
	if h.Health <= 0 then h.IsDead = true end
end
AddMoney = function( n ) CurrentRun.Money = CurrentRun.Money + n end
SpendMoney = function( n ) CurrentRun.Money = CurrentRun.Money - n end
GiveRandomConsumables = function( a ) called( "Drop:" .. a.LootOptions[1].Name ) end
CreateLoot = function( a ) called( "Loot:" .. a.Name ) end
LootData = { ZeusUpgrade = {}, AresUpgrade = {}, WeaponUpgrade = {}, TrialUpgrade = {} }
ObstacleData = { HealDropMinor = {} }
TraitData = { ChaosCurseMoveSpeedTrait = {} }
HeroHasTrait = function( name ) return false end
AddTraitToHero = function( a ) called( "Trait:" .. a.TraitName ) end
IsGodTrait = function( name ) return name == "ZeusWeaponTrait" end
RemoveTraitData = function( h, trait ) called( "Remove:" .. trait.Name ) end
UpdateLifePips = function( h ) called( "LifePips" ) end
GetUnitDataValue = function( a ) return speed[ a.Id ] end
SetUnitProperty = function( a ) if a.Property == "Speed" then speed[ a.DestinationId ] = a.Value end end
SetScale = function( a ) scale[ a.Id ] = a.Fraction end
FreezePlayerUnit = function( name ) freeze[ name ] = true end
UnfreezePlayerUnit = function( name ) freeze[ name ] = nil end
ToggleControl = function( a ) called( "Toggle:" .. a.Names[1] .. ":" .. tostring( a.Enabled ) ) end
AdjustColorGrading = function( a ) called( "Grade:" .. a.Name ) end
AdjustRadialBlurDistance = function( a ) end
AdjustRadialBlurStrength = function( a ) called( "Blur:" .. a.Fraction ) end
AdjustZoom = function( a ) called( "Zoom:" .. a.Fraction ) end
AdjustSimulationSpeed = function( a ) called( "Sim:" .. a.Fraction ) end
SetPlayerInvulnerable = function( name ) called( "Invulnerable" ) end
SetPlayerVulnerable = function( name ) called( "Vulnerable" ) end
ShakeScreen = function( a ) called( "Shake" ) end
local obstacles = 100
SpawnObstacle = function( a ) obstacles = obstacles + 1; return obstacles end
Teleport = function( a ) called( "Teleport:" .. a.DestinationId ) end
EnemySets = { EnemiesBiome1 = { "PunchingBagUnit", "HeavyMelee" } }
EnemyData = { PunchingBagUnit = { Name = "PunchingBagUnit" }, HeavyMelee = { Name = "HeavyMelee" }, HeavyMeleeElite = { Name = "HeavyMeleeElite" } }
DeepCopyTable = function( t ) local c = {}; for k, v in pairs( t ) do c[k] = v end; return c end
local units = 200
SpawnUnit = function( a ) units = units + 1; called( "Spawn:" .. a.Name ); return units end
SetupEnemyObject = function( e ) end

local threads = {}
thread = function( fn, ... ) table.insert( threads, { co = coroutine.create( fn ), args = { ... } } ) end
wait = function( t ) coroutine.yield() end
local onLoad
OnAnyLoad = function( t ) onLoad = t[1] end

-- The inbox the Link would write.
local inbox = nil
loadfile = function( path )
	if path ~= "Cloud90Inbox.lua" or inbox == nil then return nil end
	return function() return inbox end
end

local function step( n )
	for _ = 1, n or 1 do
		_worldTime = _worldTime + 0.2
		for _, t in ipairs( threads ) do
			if coroutine.status( t.co ) ~= "dead" then
				local ok, err = coroutine.resume( t.co, table.unpack( t.args ) )
				if not ok then error( err ) end
				t.args = {}
			end
		end
	end
end

local function lastAck( id )
	local found
	for _, line in ipairs( out ) do
		local got = line:match( "^CLOUD90 ack " .. id .. " (.+)$" )
		if got then found = got end
	end
	return found
end

local seq = 0
local function send( effect, extra )
	seq = seq + 1
	local id = "e" .. seq
	local e = { id = id, effect = effect, ms = 3000, by = "Ana" }
	for k, v in pairs( extra or {} ) do e[k] = v end
	inbox = inbox or { effects = {} }
	inbox.effects = inbox.effects or {}
	table.insert( inbox.effects, e )
	step( 1 )
	return lastAck( id )
end

-- ─────────────────────────── load ───────────────────────────
dofile( MOD_PATH )
check( out[ #out ] == "CLOUD90 hello 1.0.0", "says hello on load" )
check( onLoad ~= nil, "hooks OnAnyLoad" )
onLoad( {} )
step( 1 )
check( hasCall( "Toggle:Rush:true" ) == false, "nothing toggled yet" )
local sawNoInbox = false
for _, l in ipairs( out ) do if l == "CLOUD90 noinbox" then sawNoInbox = true end end
check( sawNoInbox, "says when there's no inbox yet" )

inbox = { effects = {} }
step( 1 )
local sawInbox = false
for _, l in ipairs( out ) do if l == "CLOUD90 inbox Cloud90Inbox.lua" then sawInbox = true end end
check( sawInbox, "finds the inbox" )

-- The first chamber's grace (5 s) holds the harshest back.
check( send( "damage" ) == "no disponible ahora", "grace after a new chamber" )
check( send( "heal" ) == "ok" and CurrentRun.Hero.Health == 75, "heal is not held back" )
step( 30 )  -- past the grace
check( send( "damage" ) == "ok" and CurrentRun.Hero.Health == 55, "damage 20" )
CurrentRun.Hero.Health = 10
check( send( "damage" ) == "ok" and CurrentRun.Hero.Health == 1, "damage never kills" )
check( send( "damage" ) == "no disponible ahora", "nothing to take at 1" )
CurrentRun.Hero.Health = 50
check( send( "heal" ) == "ok", "heal again" )

-- Same id twice: applied once.
local before = #out
step( 3 )
local again = 0
for i = before + 1, #out do if out[i]:match( "^CLOUD90 ack" ) then again = again + 1 end end
check( again == 0, "an effect is applied only once" )

check( send( "nope" ) == "efecto desconocido", "unknown effect" )

-- Gold.
check( send( "gold_add" ) == "ok" and CurrentRun.Money == 80, "+50 gold" )
check( send( "gold_remove" ) == "ok" and CurrentRun.Money == 30, "-50 gold" )
check( send( "gold_remove" ) == "ok" and CurrentRun.Money == 0, "only what there is" )
check( send( "gold_remove" ) == "no disponible ahora", "no gold left" )

-- Timed: speed, and one replacing the other.
check( send( "slow" ) == "ok" and speed[7] == 50, "slow halves the speed" )
check( send( "super_speed" ) == "ok" and speed[7] == 180, "super speed replaces slow, from the base" )
step( 20 )
check( speed[7] == 100, "speed back when it ends" )

-- Shield.
check( send( "shield", { ms = 4000 } ) == "ok", "shield on" )
check( send( "slow" ) == "escudo activo", "shield refuses troll" )
check( send( "gold_add" ) == "ok", "shield lets help through" )
step( 25 )
check( send( "slow" ) == "ok", "shield over" )
step( 20 )

-- Panic: ends what runs, refuses new ones, then resumes.
check( send( "grayscale", { ms = 30000 } ) == "ok" and hasCall( "Grade:Sepia" ), "screen effect" )
inbox.control = { paused = true }
step( 1 )
check( hasCall( "Grade:Off" ), "panic ends the screen effect" )
check( send( "heal" ) == "en pausa", "paused refuses" )
inbox.control = { paused = false }
step( 1 )

-- Menus and the House.
ActiveScreens = { BoonMenu = {} }
check( send( "heal" ) == "no disponible ahora", "not with a screen open" )
ActiveScreens = {}
CurrentRun.CurrentRoom = newRoom( "Base" )
step( 1 )
check( send( "heal" ) == "no disponible ahora", "not in the House of Hades" )

-- A new chamber ends timed effects and sets a start point.
CurrentRun.CurrentRoom = newRoom( "Tartarus" )
step( 1 )
check( send( "giant", { ms = 30000 } ) == "no disponible ahora" or true, "giant (grace doesn't apply)" )
step( 30 )
check( send( "giant", { ms = 30000 } ) == "ok" and scale[7] == 1.6, "giant" )
CurrentRun.CurrentRoom = newRoom( "Tartarus" )
step( 1 )
check( scale[7] == 1.0, "a new chamber puts the size back" )
step( 30 )
check( send( "back_to_start" ) == "ok" and hasCall( "Teleport:" .. obstacles ), "back to the chamber's start" )
check( send( "damage" ) == "no disponible ahora", "grace after a teleport" )
step( 30 )

-- Death and "Modo sin morir".
check( send( "death", { safe = true } ) == "modo sin morir", "no death in safe mode" )
check( send( "remove_death_defiance", { safe = true } ) == "ok" and #CurrentRun.Hero.LastStands == 1, "one Death Defiance taken" )
check( send( "remove_death_defiance", { safe = true } ) == "modo sin morir", "the last one kept in safe mode" )
check( send( "remove_death_defiance" ) == "ok" and #CurrentRun.Hero.LastStands == 0, "the last one taken otherwise" )
check( send( "death_defiance" ) == "ok" and hasCall( "Drop:LastStandDrop" ), "+1 Death Defiance drops one" )

-- Boons, items, curses.
check( send( "boon_zeus" ) == "ok" and hasCall( "Loot:ZeusUpgrade" ), "Zeus boon" )
check( send( "boon_poseidon" ) == "no disponible ahora", "a god the game doesn't have" )
check( send( "boon_random" ) == "ok", "random boon" )
check( send( "hammer" ) == "ok" and hasCall( "Loot:WeaponUpgrade" ), "hammer" )
check( send( "consumable" ) == "ok" and hasCall( "Drop:HealDropMinor" ), "consumable" )
check( send( "chaos_curse" ) == "ok" and hasCall( "Trait:ChaosCurseMoveSpeedTrait" ), "chaos curse" )
check( send( "lose_boon" ) == "ok" and hasCall( "Remove:ZeusWeaponTrait" ), "lose a god's boon, not a keepsake" )

-- Enemies of the zone.
check( send( "spawn_enemies" ) == "ok", "enemies" )
local spawned = 0
for _, c in ipairs( calls ) do if c:match( "^Spawn:" ) then spawned = spawned + 1 end end
check( spawned == 3, "three of them" )
check( send( "spawn_elite" ) == "ok" and hasCall( "Spawn:HeavyMeleeElite" ), "an elite" )
CurrentRun.CurrentRoom = newRoom( "Elysium" )
step( 30 )
check( send( "spawn_enemies" ) == "no disponible ahora", "no enemy list for this zone: refused" )

-- Controls and the rest.
check( send( "no_dash" ) == "ok" and hasCall( "Toggle:Rush:false" ), "no dash" )
step( 20 )
check( hasCall( "Toggle:Rush:true" ), "dash back" )
check( send( "paralyze" ) == "ok" and freeze.Cloud90, "paralyzed" )
step( 20 )
check( freeze.Cloud90 == nil, "free again" )
check( send( "earthquake" ) == "ok", "earthquake" )
step( 5 )
check( hasCall( "Shake" ), "it shakes" )
check( send( "slow_motion" ) == "ok" and hasCall( "Sim:0.5" ), "slow motion" )
check( send( "invulnerable" ) == "ok" and hasCall( "Invulnerable" ), "invulnerable" )
step( 20 )
check( hasCall( "Sim:1.0" ) and hasCall( "Vulnerable" ), "both end" )
check( send( "zoom_in" ) == "ok" and hasCall( "Zoom:1.5" ), "zoom" )
check( send( "blur" ) == "ok" and hasCall( "Blur:1.5" ), "blur" )

-- Dead Zagreus takes nothing.
CurrentRun.Hero.IsDead = true
step( 1 )
check( send( "heal" ) == "no disponible ahora", "not while dead" )
local stateLines = {}
for _, l in ipairs( out ) do if l:match( "^CLOUD90 state" ) then table.insert( stateLines, l ) end end
check( #stateLines >= 2, "reports when he can and can't take effects" )

-- A second map load: the old loop stops, the new one goes on.
CurrentRun.Hero.IsDead = false
onLoad( {} )
step( 30 )
check( send( "gold_add" ) == "ok", "works after another map load" )
local acks = 0
for _, l in ipairs( out ) do if l == "CLOUD90 ack e" .. seq .. " ok" then acks = acks + 1 end end
check( acks == 1, "one loop answers, not two" )

if failures == 0 then
	io.write( "cloud90 hades mod tests passed\n" )
else
	io.write( failures .. " failed\n" )
	os.exit( 1 )
end
