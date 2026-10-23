-- End to end on real MAME (tests/mame/run-kof-e2e.ps1): plays KOF 2002 by
-- itself with the cloud90 plugin loaded, writes effects to the plugin's
-- inbox like the app does, and logs the game's memory so the run can check
-- the plugin did each one. Writes results to <dir>/e2e.log.
local dir = os.getenv( "CLOUD90_DIR" )
local log = io.open( dir .. "/e2e.log", "w" )
local frame, mem, ports = 0, nil, nil
local P1HP, P2HP, P1POW, TIMER = 0x108239, 0x108439, 0x1082E3, 0x10A7D2

local function press( port, name, on )
	local f = ports[port] and ports[port].fields[name]
	if f then f:set_value( on and 1 or 0 ) end
end
local function signed( v ) return v >= 128 and v - 256 or v end

-- The inbox as the app writes it: every effect not answered yet.
local queued = {}
local function send( id, effect, ms, extra )
	local e = string.format( '{ id = "%s", effect = "%s", ms = %d, by = "test", safe = %s, arg = "" }', id, effect, ms or 3000, extra and "true" or "false" )
	table.insert( queued, e )
	local f = io.open( dir .. "/inbox.lua", "w" )
	f:write( "return { control = { paused = false }, effects = { " .. table.concat( queued, ", " ) .. " } }\n" )
	f:close()
	log:write( string.format( "sent %s %s at round s=?\n", id, effect ) )
end

local roundSince
local steps = {
	{ 4, function() send( "e1", "damage" ) end },                       -- in grace (3 s)? no: 4 s in
	{ 5, function() log:write( string.format( "after damage p1hp=%d\n", signed( mem:read_u8( P1HP ) ) ) ) end },
	{ 6, function() send( "e2", "power_full" ) end },
	{ 7, function() log:write( string.format( "after power_full stocks=%d\n", mem:read_u8( P1POW ) ) ) end },
	{ 7.5, function() log:write( string.format( "before invincible p1hp=%d\n", signed( mem:read_u8( P1HP ) ) ) ) end },
	{ 8, function() send( "e3", "invincible", 5000 ) end },
	{ 9, function() send( "e10", "invert_controls", 3000 ) end },
	{ 10, function() send( "e8", "rival_heal" ) end },
	{ 12, function() log:write( string.format( "during invincible p1hp=%d\n", signed( mem:read_u8( P1HP ) ) ) ) end },
	{ 14, function() send( "e4", "timer_freeze", 4000 ) end },
	{ 15, function() log:write( string.format( "freeze start timer=%02X\n", mem:read_u8( TIMER ) ) ) end },
	{ 17, function() log:write( string.format( "freeze later timer=%02X\n", mem:read_u8( TIMER ) ) ) end },
	{ 18, function() send( "e9", "nope" ) end },
	{ 19, function() send( "e7", "ko", 1000, true ) end },
	{ 20, function() send( "e5", "shield", 6000 ) end },
	{ 21, function() send( "e6", "damage" ) end },
	{ 26, function() log:write( "done\n" ); log:flush(); manager.machine:exit() end },
}
local nextStep = 1

KOF_E2E_SUB = emu.add_machine_frame_notifier( function()
	if not mem then
		mem = manager.machine.devices[":maincpu"].spaces["program"]
		ports = manager.machine.ioport.ports
	end
	frame = frame + 1
	local f = frame
	press( ":AUDIO_COIN", "Coin 1", f >= 600 and f < 608 )
	press( ":edge:joy:START", "1 Player Start", f >= 700 and f < 708 )
	if f > 760 then press( ":edge:joy:JOY1", "P1 A", ( f % 12 ) < 3 ) end
	local a, b = signed( mem:read_u8( P1HP ) ), signed( mem:read_u8( P2HP ) )
	if roundSince == nil and f > 760 and a >= 1 and a <= 102 and b >= 1 and b <= 102 then
		roundSince = f
		log:write( "round on\n" )
	end
	if roundSince then
		local s = ( f - roundSince ) / 60
		while steps[nextStep] and s >= steps[nextStep][1] do
			steps[nextStep][2]()
			nextStep = nextStep + 1
			log:flush()
		end
	end
end )
