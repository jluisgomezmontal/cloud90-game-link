-- Plays KOF 2002 by itself for the app's end-to-end test: coin, start,
-- mash A through the selects and the fight. Nothing else.
local frame, ports = 0, nil
local function press( port, name, on )
	local f = ports[port] and ports[port].fields[name]
	if f then f:set_value( on and 1 or 0 ) end
end
KOF_PLAY_SUB = emu.add_machine_frame_notifier( function()
	ports = ports or manager.machine.ioport.ports
	frame = frame + 1
	press( ":AUDIO_COIN", "Coin 1", frame >= 600 and frame < 608 )
	press( ":edge:joy:START", "1 Player Start", frame >= 700 and frame < 708 )
	if frame > 760 then press( ":edge:joy:JOY1", "P1 A", ( frame % 12 ) < 3 ) end
end )
