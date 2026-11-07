local S=assert(require('cloud90').state)
local ports=manager.machine.ioport.ports
local dir=os.getenv('CLOUD90_DIR');local f,done=0,nil
local function press(name,on)for _,p in pairs(ports)do local field=p.fields[name];if field then field:set_value(on and 1 or 0)end end end
MVSC_PLAY_SUB=emu.add_machine_frame_notifier(function()
    f=f+1
    press('Coin 1',f>=1800 and f<1960 and f%40<10)
    press('1 Player Start',f>=2200 and f<2210)
    press('P1 Button 1',f>2260 and not S.playing and f%12<3)
    if not done and S.active.timer_freeze then done=f end
    if done and f>done+300 then manager.machine:exit()end
end)
