-- End to end on real MAME (tests/mame/run-mslug3-e2e.ps1): plays Metal Slug 3
-- by itself with the cloud90 plugin loaded, writes effects to the plugin's
-- inbox like the app does, and logs the game's memory so the run can check
-- the plugin did each one. Writes results to <dir>/e2e.log.
local dir = os.getenv( "CLOUD90_DIR" )
local log = io.open( dir .. "/e2e.log", "w" )
local frame, mem, ports = 0, nil, nil
local MODE, LIVES, GRENADES, TIMER, INVUL, WEAPON, AMMO = 0x1002BE, 0x1002BB, 0x10EAF5, 0x10ED78, 0x100581, 0x10EAF3, 0x10EAFA
local BASE, SIZE, ROOT = 0x100700, 0xB0, 0x10044C

local function press( port, name, on )
	local f = ports[port] and ports[port].fields[name]
	if f then f:set_value( on and 1 or 0 ) end
end

-- The inbox as the app writes it: every effect not answered yet.
local queued = {}
local function send( id, effect, ms, arg )
	table.insert( queued, string.format( '{ id = "%s", effect = "%s", ms = %d, by = "test", safe = false, arg = "%s" }', id, effect, ms or 3000, arg or "" ) )
	local f = io.open( dir .. "/inbox.lua", "w" )
	f:write( "return { control = { paused = false }, effects = { " .. table.concat( queued, ", " ) .. " } }\n" )
	f:close()
	log:write( "sent " .. id .. " " .. effect .. "\n" )
end
local function note( fmt, ... ) log:write( string.format( fmt, ... ) .. "\n" ) end

local function enemies()
	local n = 0
	for k = 0, 111 do
		local s = BASE + k * SIZE
		if mem:read_u32( s + 0x0C ) ~= 0x567A and mem:read_u32( s + 0x44 ) == 0x10C120 then n = n + 1 end
	end
	return n
end
-- The task list must stay a closed ring with matching back links.
local function listOk()
	local n, node = 0, mem:read_u32( ROOT + 4 )
	while node ~= ROOT do
		if node < 0x100000 or node > 0x10FFFF or n > 300 then return false end
		if mem:read_u32( mem:read_u32( node + 4 ) + 8 ) ~= node then return false end
		node = mem:read_u32( node + 4 ); n = n + 1
	end
	return true
end

local playSince
local spawnSent = false
local steps = {
	{ 2, function() send( "e1", "invincible", 60000 ) end },
	{ 3, function() note( "invul=%d", mem:read_u8( INVUL ) ) end },
	{ 4, function() note( "before grenades=%d lives=%d", mem:read_u8( GRENADES ), mem:read_u8( LIVES ) ) end },
	{ 4.2, function() send( "e2", "grenades_add" ); send( "e3", "life_add" ) end },
	{ 5, function() note( "after add grenades=%d lives=%d", mem:read_u8( GRENADES ), mem:read_u8( LIVES ) ) end },
	{ 5.2, function() send( "e4", "grenades_remove" ); send( "e5", "life_remove" ) end },
	{ 6, function() note( "after remove grenades=%d lives=%d", mem:read_u8( GRENADES ), mem:read_u8( LIVES ) ) end },
	{ 6.2, function() send( "e6", "give_weapon", 3000, "shotgun" ) end },
	{ 7, function() note( "weapon=%d ammo=%d", mem:read_u8( WEAPON ), mem:read_u16( AMMO ) ) end },
	{ 7.2, function() send( "e7", "ammo_add" ) end },
	{ 8, function() note( "after ammo weapon=%d ammo=%d", mem:read_u8( WEAPON ), mem:read_u16( AMMO ) ) end },
	{ 8.2, function() send( "e8", "remove_weapon" ) end },
	{ 9, function() note( "after remove weapon=%d ammo=%d", mem:read_u8( WEAPON ), mem:read_u16( AMMO ) ) end },
	{ 9.2, function() send( "e9", "remove_weapon" ) end },
	{ 10, function() send( "e10", "timer_freeze", 4000 ) end },
	{ 10.5, function() note( "freeze start timer=%02X", mem:read_u8( TIMER ) ) end },
	{ 13, function() note( "freeze later timer=%02X", mem:read_u8( TIMER ) ) end },
	{ 13.5, function() send( "e11", "no_shoot", 2000 ); send( "e12", "nope" ) end },
	{ 14, function() note( "invul later=%d", mem:read_u8( INVUL ) ) end },
	{ 50, function() note( "spawn %s", spawnSent and "sent" or "never (no enemies)" ) end },
	{ 54, function() note( "after spawn enemies=%d list=%s", enemies(), listOk() and "ok" or "BROKEN" ) end },
	{ 55, function() send( "e14", "shield", 6000 ) end },
	{ 56, function() send( "e15", "grenades_remove" ) end },
	{ 58, function() note( "end list=%s mode=%d", listOk() and "ok" or "BROKEN", mem:read_u8( MODE ) ); log:write( "done\n" ); log:flush(); manager.machine:exit() end },
}
local nextStep = 1

MSLUG3_E2E_SUB = emu.add_machine_frame_notifier( function()
	if not mem then
		mem = manager.machine.devices[":maincpu"].spaces["program"]
		ports = manager.machine.ioport.ports
	end
	frame = frame + 1
	local f = frame
	press( ":AUDIO_COIN", "Coin 1", f >= 600 and f < 608 )
	press( ":edge:joy:START", "1 Player Start", f >= 700 and f < 708 )
	if f > 760 then press( ":edge:joy:JOY1", "P1 Right", f % 120 < 40 ) end
	if playSince == nil and f > 760 and mem:read_u8( MODE ) == 2 then
		playSince = f
		log:write( "playing\n" )
	end
	if not playSince then return end
	local s = ( f - playSince ) / 60
	-- Enemies come when the beach does: spawn two as soon as there are some.
	if not spawnSent and s >= 15 and s < 50 and enemies() > 0 then
		spawnSent = true
		note( "before spawn enemies=%d", enemies() )
		send( "e13", "spawn_enemies", 3000, "2" )
	end
	while steps[nextStep] and s >= steps[nextStep][1] do
		steps[nextStep][2]()
		nextStep = nextStep + 1
		log:flush()
	end
end )
