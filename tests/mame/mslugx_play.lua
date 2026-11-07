local f=0;local mem=manager.machine.devices[':maincpu'].spaces.program
local function press(name,on)for _,p in pairs(manager.machine.ioport.ports)do local v=p.fields[name];if v then v:set_value(on and 1 or 0)end end end
MSX_PLAY=emu.add_machine_frame_notifier(function()
 f=f+1;press('Coin 1',f>=600 and f<610);press('1 Player Start',f>=720 and f<730)
 press('P1 A',f>800 and f<1250 and f%30<5)
 if f>2100 then manager.machine:exit()end
end)
