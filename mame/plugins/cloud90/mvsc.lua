-- Cloud90 adapter for MAME 0.289 / mvscu USA 980123. Independently written;
-- memory facts and their native validation are documented in mod/mvsc/README.md.
local M = {}
local controls = {'Up','Down','Left','Right','Button 1','Button 2','Button 3','Button 4','Button 5','Button 6'}
local timed = {timer_freeze=20,invert_controls=10,no_punch=8,no_kick=8,no_jump=8,
    auto_jump=10,tunnel_vision=10,blackout=3,slow_motion=10,shield=60}
local trolls = {damage=true,rival_heal=true,power_empty=true,rival_power=true,timer_remove=true,
    invert_controls=true,no_punch=true,no_kick=true,no_jump=true,auto_jump=true,
    tunnel_vision=true,blackout=true,slow_motion=true}
local effects = {'heal','damage','rival_heal','power_add','power_empty','rival_power',
    'timer_add','timer_remove','timer_freeze','invert_controls','no_punch','no_kick',
    'no_jump','auto_jump','tunnel_vision','blackout','slow_motion','shield'}
local allowed = {}; for _,id in ipairs(effects) do allowed[id]=true end
local function integer(v,lo,hi) return type(v)=='number' and v%1==0 and v>=lo and v<=hi end

function M.new(env)
    local S={session=env.session,epoch=env.session..'_0',sequence=0,playing=false,paused=true,
        active={},seen={},order={},frame=0,noDeath=true,healthy=false}
    local mem=env.mem
    local function point(side)
        local p=mem:read_u32(0xff40c8+side*8)&0xffffff
        local base=0xff3000+side*0x400
        if p~=base and p~=base+0x800 then return nil end
        if mem:read_u8(p+0x281)~=0 then return nil end
        return p
    end
    local function partner(p) return p>=0xff3800 and p-0x800 or p+0x800 end
    local function clock()
        local v=mem:read_u8(0xff4008)
        if v>>4>9 or v&15>9 then return nil end
        return (v>>4)*10+(v&15)
    end
    local function setClock(n) mem:write_u8(0xff4008,(n//10)*16+n%10) end
    local function clearControls()
        for _,name in ipairs(controls) do local f=env.fields[name]; if f then f:clear_value() end end
    end
    local function clear()
        if S.active.slow_motion and env.video.throttle_rate==0.5 then env.video.throttle_rate=S.active.slow_motion.previous end
        S.active={}; clearControls()
    end
    function S.invalidate()
        clear();S.sequence=S.sequence+1;S.epoch=S.session..'_'..S.sequence;S.playing=false
    end
    function S.stop() S.invalidate();S.healthy=false;S.paused=true end
    function S.inPlay()
        if env.blocked() or mem:read_u8(0xff4100)~=1 or mem:read_u8(0xff4101)~=1
            or mem:read_u16(0xff4104)~=8 or mem:read_u16(0xff4106)~=4
            or mem:read_u8(0xff4000)~=8 then return false end
        local a,b=point(0),point(1);local c=clock()
        if not a or not b or not c or c<1 then return false end
        return integer(mem:read_u16(a+0x270),1,144) and integer(mem:read_u16(b+0x270),1,144)
    end
    function S.refresh()
        local playing=S.inPlay()
        local stage=mem:read_u8(0xff4113)
        if playing~=S.playing or (playing and S.stage~=stage) then
            S.invalidate();if playing then S.graceUntil=env.time()+3 end
        end
        S.playing=playing;S.stage=stage
        if not playing or S.paused or not S.healthy then clear() end
    end
    local function setHealth(p,delta)
        local hp=mem:read_u16(p+0x270)
        local nextHp=math.max(1,math.min(144,hp+delta))
        if nextHp==hp then return false,'vida al limite' end
        local red=mem:read_u16(p+0x27a)
        mem:write_u16(p+0x270,nextHp)
        mem:write_u16(p+0x27a,math.max(nextHp,math.min(144,red+nextHp-hp)))
        return true
    end
    local function meter(side,empty)
        local p=point(side);local level=mem:read_u8(p+0x274);local bar=mem:read_u16(p+0x272)
        if not integer(level,0,3) or not integer(bar,0,144) then return false,'medidor no compatible' end
        if empty and level==0 and bar==0 then return false,'medidor vacio' end
        if not empty and level==3 then return false,'medidor lleno' end
        level=empty and 0 or math.min(3,level+1)
        bar=(empty or level==3) and 0 or bar
        for _,at in ipairs({p,partner(p)}) do mem:write_u8(at+0x274,level);mem:write_u16(at+0x272,bar) end
        return true
    end
    local function extend(id,ms)
        local now=env.time();local limit=timed[id]
        local old=S.active[id]
        if not old then
            old={};S.active[id]=old
            if id=='timer_freeze' then old.clock=clock() end
            if id=='slow_motion' then old.previous=env.video.throttle_rate;env.video.throttle_rate=0.5 end
        end
        old.untilT=math.min(now+limit,math.max(now,old.untilT or now)+math.max(0.001,math.min(ms/1000,limit)))
    end
    local function apply(e,now)
        if not allowed[e.effect] then return false,'efecto desconocido' end
        if not S.healthy or S.paused then return false,'efectos en pausa' end
        if not S.playing then return false,'no hay combate individual activo' end
        if e.epoch~=S.epoch then return false,'combate anterior' end
        if not integer(e.expiresUtcMs,now-10000,now+11000) or e.expiresUtcMs<=now then return false,'comando vencido' end
        if not integer(e.ms,0,120000) or type(e.safe)~='boolean' then return false,'comando invalido' end
        local id=e.effect
        if trolls[id] and S.active.shield then return false,'escudo activo' end
        if trolls[id] and env.time()<(S.graceUntil or 0) then return false,'inicio de combate' end
        if id=='heal' then return setHealth(point(0),36)
        elseif id=='damage' then return setHealth(point(0),-22)
        elseif id=='rival_heal' then return setHealth(point(1),36)
        elseif id=='power_add' then return meter(0,false)
        elseif id=='power_empty' then return meter(0,true)
        elseif id=='rival_power' then return meter(1,false)
        elseif id=='timer_add' then
            if clock()>=99 then return false,'reloj al maximo' end
            setClock(math.min(99,clock()+20));if S.active.timer_freeze then S.active.timer_freeze.clock=clock() end
        elseif id=='timer_remove' then
            if clock()<=10 then return false,'reloj al minimo' end
            setClock(math.max(10,clock()-20));if S.active.timer_freeze then S.active.timer_freeze.clock=clock() end
        elseif timed[id] then
            if id=='shield' then
                for bad in pairs(trolls) do
                    if bad=='slow_motion' and S.active[bad] and env.video.throttle_rate==0.5 then env.video.throttle_rate=S.active[bad].previous end
                    S.active[bad]=nil
                end
                clearControls()
            elseif id=='blackout' then S.active.tunnel_vision=nil
            elseif id=='tunnel_vision' then S.active.blackout=nil end
            extend(id,e.ms)
        end
        return true
    end
    function S.receive(inbox)
        local now=env.wall()
        if type(inbox)~='table' or inbox.session~=S.session or not integer(inbox.heartbeatUtcMs,now-4000,now+1500)
            or type(inbox.paused)~='boolean' or type(inbox.noDeath)~='boolean' or type(inbox.effects)~='table' or #inbox.effects>20 then
            S.healthy=false;clear();return
        end
        S.lastHeartbeat=inbox.heartbeatUtcMs;S.healthy=true;S.paused=inbox.paused;S.noDeath=inbox.noDeath
        S.refresh()
        for _,e in ipairs(inbox.effects) do
            if type(e)=='table' and type(e.id)=='string' and #e.id<=80 and e.id:match('^[A-Za-z0-9_-]+$') then
                local result=S.seen[e.id]
                if not result then
                    local good,ok,reason=pcall(apply,e,now)
                    result=good and ok and 'ok' or (good and reason or 'error del adaptador')
                    S.seen[e.id]=result;S.order[#S.order+1]=e.id
                    if #S.order>512 then S.seen[table.remove(S.order,1)]=nil end
                end
                env.ack(e.id,result)
            end
        end
    end
    function S.capabilities() return S.playing and S.healthy and effects or {} end
    function S.tick()
        S.frame=S.frame+1
        local prior=S.playing;S.refresh()
        if S.playing and not prior then S.graceUntil=env.time()+3 end
        if not S.lastHeartbeat or math.abs(env.wall()-S.lastHeartbeat)>4000 then S.healthy=false;clear() end
        if not S.playing or S.paused or not S.healthy then return end
        local now=env.time()
        for id,t in pairs(S.active) do
            if now>=t.untilT then
                if id=='slow_motion' and env.video.throttle_rate==0.5 then env.video.throttle_rate=t.previous end
                S.active[id]=nil
            end
        end
        if S.active.timer_freeze then setClock(S.active.timer_freeze.clock) end
        local wants={};local any=false
        for _,id in ipairs({'invert_controls','no_punch','no_kick','no_jump','auto_jump'}) do if S.active[id] then any=true end end
        if not any then if S.touched then clearControls();S.touched=false end;return end
        for _,name in ipairs(controls) do wants[name]=env.real(name) end
        if S.active.invert_controls then wants.Left,wants.Right=wants.Right,wants.Left end
        if S.active.no_punch then for i=1,3 do wants['Button '..i]=false end end
        if S.active.no_kick then for i=4,6 do wants['Button '..i]=false end end
        if S.active.auto_jump then wants.Up=wants.Up or S.frame%40<4 end
        if S.active.no_jump then wants.Up=false end
        for _,name in ipairs(controls) do env.fields[name]:set_value(wants[name] and 1 or 0) end
        S.touched=true
    end
    function S.draw()
        local screen=env.screen;if not screen then return end
        local w,h=screen.width,screen.height
        if S.active.blackout then screen:draw_box(0,0,w,h,0xff000000,0xff000000)
        elseif S.active.tunnel_vision then
            local x,y=w*0.22,h*0.22
            for _,b in ipairs({{0,0,w,y},{0,h-y,w,h},{0,y,x,h-y},{w-x,y,w,h-y}}) do screen:draw_box(b[1],b[2],b[3],b[4],0xee000000,0xee000000) end
        end
        if S.active.blackout or S.active.shield then screen:draw_text('center',2,S.active.shield and 'ESCUDO CLOUD90' or 'F9: pausar efectos',0xff7dd3fc,0x80000000) end
    end
    return S
end

function M.startplugin(owner)
    require('cloud90/session').start(owner,{id='mvsc',system='mvscu',controls=controls,create=M.new})
end
return M
