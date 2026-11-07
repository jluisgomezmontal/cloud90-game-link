-- Real MAME integration test. Uses only the owner's ROMs, isolated config/NVRAM.
local dir=assert(os.getenv('CLOUD90_DIR'));local session=assert(os.getenv('CLOUD90_SESSION'))
local json=require('json');local log=assert(io.open(dir..'/e2e.log','w'))
local S=assert(require('cloud90').state,'MVSC adapter did not start')
local mem=manager.machine.devices[':maincpu'].spaces.program
local ports=manager.machine.ioport.ports
local frame,since,serial,failures,nextStep=0,nil,0,0,1
local batch={};local paused=false
local function check(ok,label)
    log:write((ok and 'PASS ' or 'FAIL ')..label..'\n');log:flush()
    if not ok then failures=failures+1 end
end
local function press(name,on)
    for _,port in pairs(ports) do local field=port.fields[name];if field then field:set_value(on and 1 or 0) end end
end
local function writeInbox()
    local f=assert(io.open(dir..'/inbox.json','w'))
    f:write(json.stringify({session=session,heartbeatUtcMs=os.time()*1000,paused=paused,noDeath=true,effects=batch}));f:close()
end
local function command(id,ms,epoch)
    serial=serial+1
    return{id='native_'..serial,effect=id,ms=ms or 1000,safe=true,epoch=epoch or S.epoch,expiresUtcMs=os.time()*1000+10000}
end
local function direct(id,ms,epoch)
    local e=command(id,ms,epoch)
    S.receive({session=session,heartbeatUtcMs=os.time()*1000,paused=false,noDeath=true,effects={e}})
    return e
end
local function point(side)return mem:read_u32(0xff40c8+side*8)&0xffffff end
local function ready()
    return mem:read_u8(0xff4100)==1 and mem:read_u8(0xff4101)==1 and mem:read_u8(0xff4000)==8
end
local tagBefore,tagSeen,freezeValue,epochBefore,pausedEpoch
local steps={
    {240,function()
        check(S.playing and #S.capabilities()==18,'real fight and all 18 capabilities detected')
        local p=point(0);local reserve=p>=0xff3800 and p-0x800 or p+0x800
        local oldReserve=mem:read_u16(reserve+0x270)
        mem:write_u16(p+0x270,60);mem:write_u16(p+0x27a,80)
        local e=direct('heal');check(mem:read_u16(p+0x270)==96,'heal is +36 HP on the active record')
        S.receive({session=session,heartbeatUtcMs=os.time()*1000,paused=false,noDeath=true,effects={e}})
        check(mem:read_u16(p+0x270)==96 and mem:read_u16(reserve+0x270)==oldReserve,'duplicate heals once and leaves reserve intact')
        direct('damage');check(mem:read_u16(p+0x270)==74,'damage is 22 HP')
        mem:write_u16(p+0x270,144);mem:write_u16(p+0x27a,144)
        local rival=point(1);mem:write_u16(rival+0x270,60);mem:write_u16(rival+0x27a,90)
        direct('rival_heal');check(mem:read_u16(rival+0x270)==96,'rival heal targets CPU active')
        for _,at in ipairs({0xff3000,0xff3800}) do mem:write_u8(at+0x274,0);mem:write_u16(at+0x272,0) end
        direct('power_add');check(mem:read_u8(0xff3274)==1 and mem:read_u8(0xff3a74)==1,'super updates both team records')
        direct('power_empty');check(mem:read_u8(0xff3274)==0 and mem:read_u16(0xff3a72)==0,'super empty clears levels and progress')
        for _,at in ipairs({0xff3400,0xff3c00}) do mem:write_u8(at+0x274,0);mem:write_u16(at+0x272,0) end
        direct('rival_power');check(mem:read_u8(0xff3674)==1 and mem:read_u8(0xff3e74)==1,'rival super updates CPU team')
        mem:write_u8(0xff4008,0x50);direct('timer_add');check(mem:read_u8(0xff4008)==0x70,'clock +20 in BCD')
        direct('timer_remove');check(mem:read_u8(0xff4008)==0x50,'clock -20 in BCD')
        direct('timer_freeze',1000);freezeValue=mem:read_u8(0xff4008)
    end},
    {270,function()check(mem:read_u8(0xff4008)==freezeValue,'clock held across real game frames') end},
    {330,function()
        check(not S.active.timer_freeze,'timer freeze expires')
        for _,id in ipairs({'invert_controls','no_punch','no_kick','no_jump','auto_jump','tunnel_vision'})do direct(id);check(S.active[id]~=nil,id..' accepts native input/screen environment')end
        direct('blackout');check(S.active.blackout and not S.active.tunnel_vision,'blackout replaces tunnel')
        local rate=manager.machine.video.throttle_rate;direct('slow_motion');check(manager.machine.video.throttle_rate==0.5,'native slowdown')
        direct('shield');check(manager.machine.video.throttle_rate==rate and not S.active.no_punch and not S.active.blackout,'shield clears sabotage and restores speed')
        local p=point(0);local hp=mem:read_u16(p+0x270);direct('damage');check(mem:read_u16(p+0x270)==hp,'shield rejects direct damage')
        S.receive({session=session,heartbeatUtcMs=os.time()*1000,paused=true,noDeath=true,effects={}})
        check(next(S.active)==nil,'panic clears shield and all remaining effects')
        tagBefore=point(0)
    end},
    {540,function()
        check(tagSeen,'native strong punch + strong kick changes active character')
        if tagSeen then
            local p=point(0);local other=p>=0xff3800 and p-0x800 or p+0x800
            mem:write_u16(p+0x270,60);mem:write_u16(p+0x27a,60);local hp=mem:read_u16(other+0x270)
            direct('heal');check(mem:read_u16(p+0x270)==96 and mem:read_u16(other+0x270)==hp,'healing follows the tagged-in fighter')
        end
        local p=point(0);mem:write_u16(p+0x270,50);mem:write_u16(p+0x27a,80)
        batch={command('heal')};writeInbox()
    end},
    {555,function()
        local f=assert(io.open(dir..'/acks.txt','r'));local ack=f:read('a');f:close()
        check(ack:find('ack '..batch[1].id..' ok',1,true)~=nil,'JSON inbox effect confirmed by real plugin ACK')
        batch={};epochBefore=S.epoch
        local p=point(0);local hp=mem:read_u16(p+0x270);direct('damage',1000,'old_attempt');check(mem:read_u16(p+0x270)==hp,'stale epoch rejected')
        direct('no_punch',5000)
        manager.machine:save('cloud90-test')
    end},
    {600,function() manager.machine:load('cloud90-test') end},
    {630,function()end},
}
-- Pause/resume is tested through real MAME notifiers using the periodic hook.
local resumePending=false
MVSC_E2E_PERIODIC=function()
    if resumePending and manager.machine.paused then
        resumePending=false;check(not S.playing and next(S.active)==nil,'MAME pause clears effects');emu.unpause()
    end
end
emu.register_periodic(MVSC_E2E_PERIODIC)
-- Replace the last step's coroutine wait with a real pause/resume request.
steps[#steps][2]=function()
    check(S.epoch~=epochBefore and not S.active.no_punch,'actual savestate load invalidates epoch and clears effects')
    pausedEpoch=S.epoch;resumePending=true;emu.pause()
end
MVSC_E2E_SUB=emu.add_machine_frame_notifier(function()
    frame=frame+1
    press('Coin 1',frame>=1800 and frame<1960 and frame%40<10)
    press('1 Player Start',frame>=2200 and frame<2210)
    if not since then press('P1 Button 1',frame>2260 and frame%12<3) end
    if not since and ready() then since=frame;press('P1 Button 1',false) end
    if since then
        local n=frame-since
        -- Retry a normal native tag while neutral, not by swapping RAM pointers.
        if tagBefore and point(0)~=tagBefore then tagSeen=true end
        press('P1 Button 3',n>=360 and n<500 and n%40<6 and not tagSeen)
        press('P1 Button 6',n>=360 and n<500 and n%40<6 and not tagSeen)
        while steps[nextStep] and n>=steps[nextStep][1]do
            local ok,err=pcall(steps[nextStep][2]);check(ok,'test step '..nextStep..(ok and '' or ': '..tostring(err)));nextStep=nextStep+1
        end
        if n>=690 then
            check(S.epoch~=pausedEpoch,'MAME resume invalidates context')
            check(ready(),'real fight survives all modifier checks')
            log:write('RESULT failures='..failures..'\n');log:close();manager.machine:exit();return
        end
    end
    if frame%3==0 then writeInbox() end
    if frame>7000 then check(false,'timed out entering fight');log:write('RESULT failures='..failures..'\n');log:close();manager.machine:exit() end
end)
