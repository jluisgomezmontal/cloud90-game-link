-- Shared JSON transport and native MAME lifecycle. Game adapters own all memory writes.
local M={}
function M.start(owner,config)
    local controls=config.controls
    local machine=manager.machine;local dir=os.getenv('CLOUD90_DIR');local session=os.getenv('CLOUD90_SESSION')
    if machine.system.name~=config.system or not dir or not session or not session:match('^[a-f0-9]+$') or #session~=32 or emu.app_version()~='0.289' then
        print('CLOUD90 requiere Game Link y MAME 0.289');return
    end
    local json=require('json');local fields={}
    for _,port in pairs(machine.ioport.ports) do
        for _,name in ipairs(controls) do local f=port.fields['P1 '..name];if f then fields[name]=f end end
    end
    for _,name in ipairs(controls) do if not fields[name] then error('faltan controles: '..name) end end
    local function say(line)
        local f=io.open(dir..'/acks.txt','a');if f then f:write(line..'\n');f:close() end
    end
    local lastRead,lastStatus=-1,-1
    local readFrames=0;local acked,ackOrder={},{}
    local env={session=session,mem=machine.devices[':maincpu'].spaces.program,fields=fields,
        video=machine.video,screen=machine.screens[':screen'],time=function() return machine.time:as_double() end,
        wall=function() return os.time()*1000 end,
        blocked=function() return machine.paused or manager.ui.menu_active end,
        real=function(name) return machine.input:seq_pressed(fields[name]:input_seq('standard')) end,
        ack=function(id,result)
            if not acked[id] then
                acked[id]=true;ackOrder[#ackOrder+1]=id
                if #ackOrder>512 then acked[table.remove(ackOrder,1)]=nil end
                say('ack '..id..' '..result)
            end
        end}
    local S=config.create(env);owner.state=S
    local function readInbox()
        local f=io.open(dir..'/inbox.json','r');if not f then S.receive(nil);return end
        local text=f:read(65537);f:close()
        if not text or #text>65536 then S.receive(nil);return end
        local ok,inbox=pcall(json.parse,text);S.receive(ok and inbox or nil)
    end
    local function status(force)
        local now=env.time();local wall=os.time()
        if not force and now-lastStatus<0.5 and wall==lastRead then return end
        lastStatus=now;lastRead=wall
        say(config.id..'_state '..session..' '..S.epoch..' '..(S.playing and S.healthy and '1' or '0')..' '..table.concat(S.capabilities(),','))
    end
    local function guard(fn)
        return function()
            local ok,err=pcall(fn)
            if not ok then S.stop();status(true);if not S.reported then S.reported=true;say('error '..tostring(err)) end end
        end
    end
    owner.subscriptions=owner.subscriptions or {}
    local function subscribe(fn,cb) table.insert(owner.subscriptions,fn(guard(cb))) end
    subscribe(emu.add_machine_frame_notifier,function()
        readFrames=readFrames+1
        if readFrames%6==0 then readInbox() end
        S.tick();status(false)
    end)
    subscribe(emu.add_machine_post_load_notifier,function() S.invalidate();status(true) end)
    subscribe(emu.add_machine_reset_notifier,function() S.invalidate();status(true) end)
    subscribe(emu.add_machine_pause_notifier,function() S.invalidate();status(true) end)
    subscribe(emu.add_machine_resume_notifier,function() S.invalidate();status(true) end)
    subscribe(emu.add_machine_stop_notifier,function() S.stop();status(true) end)
    emu.register_frame_done(guard(function() S.draw() end))
    emu.register_periodic(guard(function()
        if machine.paused or manager.ui.menu_active then readInbox();S.refresh();status(false) end
    end))
    say('hello cloud90-'..config.id..'-1 '..config.system);status(true)
end
return M
