-- Cloud90's SMW bridge. Runs in the runtime's restricted Lua VM, on the game thread.
-- No filesystem, network, ROM patch, or token. Installed in memory for one launch.
if cloud90_smw then cloud90_smw.stop() end
event.unregisterbyname('cloud90.smw')
local r, w = mainmemory.read_u8, mainmemory.write_u8
local r16 = mainmemory.read_u16_le
local C = { session = CLOUD90_SMW_SESSION, sequence = 0, epoch = '', playing = false,
    paused = true, safe = true, connected = false, lease = 0, active = {}, seen = {}, order = {} }
assert(type(C.session) == 'string' and C.session:match('^[a-f0-9]+$'), 'invalid session')
C.epoch=C.session..'_0'
cloud90_smw = C
local harmful = {small=true,spawn_enemy=true,spawn_wave=true,time_subtract=true,
    invert_controls=true,auto_jump=true,super_speed=true,high_jump=true}
local duration = {invincible=30,fire_stream=15,invert_controls=10,auto_jump=10,
    super_speed=10,high_jump=10,shield=60}
local ordinary = {'mushroom','fire_flower','cape','small','invincible','life_add','shield'}
local enemy_types = {[0x04]=true,[0x05]=true,[0x06]=true,[0x07]=true,[0x0f]=true}
local function fire_reset() game.command('fire_stream_reset') end
local function clear()
    C.active = {}
    fire_reset()
    -- Never clear a star or a legitimate longer damage-protection timer.
    if C.invincibleWritten and r(0x1497) <= 2 then w(0x1497,0) end
    C.invincibleWritten=false
end
function C.stop() clear(); C.connected=false; C.paused=true end
local function in_level()
    return r(0x100)==0x14 and r(0x109)==0 and r(0xdb2)==0 and r(0x71)==0
        and r(0x9d)==0 and r(0x13d4)==0 and r(0x1493)==0 and r(0x1434)==0
end
local function land()
    return r(0x75)==0 and r(0x187a)==0 and r(0x1407)==0 and r(0x13f3)==0
        and r(0x13fc)==0 and r(0x0d9b)==0
end
local function clock()
    local a,b,c=r(0xf31),r(0xf32),r(0xf33)
    if a>9 or b>9 or c>9 then return 0 end
    return a*100+b*10+c
end
local function set_clock(t)
    w(0xf31,math.floor(t/100)); w(0xf32,math.floor(t/10)%10); w(0xf33,t%10)
end
local function source_enemy()
    if not land() then return nil end
    local camera = r16(0x1a)
    for s=0,9 do
        local x=r(0xe4+s)+256*r(0x14e0+s)
        if r(0x14c8+s)==8 and enemy_types[r(0x9e+s)] and r(0x15a0+s)==0
            and r(0x186c+s)==0 and x>=camera and x<camera+256 then return s end
    end
end
local function slots()
    local n=0
    for s=0,9 do if r(0x14c8+s)==0 then n=n+1 end end
    return n
end
local function caps()
    local list={}
    if not C.playing then return list end
    for _,id in ipairs(ordinary) do list[#list+1]=id end
    list[#list+1]='invert_controls'
    if clock()>0 then list[#list+1]='time_add' end
    if clock()>10 then list[#list+1]='time_subtract' end
    if land() then
        for _,id in ipairs({'fire_stream','auto_jump','super_speed','high_jump'}) do list[#list+1]=id end
        if source_enemy() and slots()>=1 then list[#list+1]='spawn_enemy' end
        if source_enemy() and slots()>=3 then list[#list+1]='spawn_wave' end
    end
    return list
end
local function refresh()
    local playing=in_level()
    local frame=r(0x13)
    local level=tostring(r16(0x10b))..'_'..tostring(r(0x13bf))
    -- Guest frame discontinuities identify most savestate loads/resets. Saved
    -- states are not supported while connected; host monotonic frames aren't saved.
    local discontinuity=C.frame and ((frame-C.frame)%256)>1
    if playing~=C.playing or (playing and (level~=C.level or discontinuity)) then
        clear(); C.sequence=C.sequence+1; C.epoch=C.session..'_'..C.sequence
    end
    C.playing=playing; C.level=level; C.frame=frame
    if not playing or not C.connected or C.paused then clear() end
end
-- Copy only a supported, already initialized on-screen sprite. This preserves
-- the level's graphics and collision tables, without inventing ROM INIT calls.
local sprite_fields={0x9e,0xaa,0xb6,0xc2,0x1504,0x1510,0x151c,0x1528,0x1534,
    0x1540,0x154c,0x1558,0x1564,0x1570,0x157c,0x1588,0x1594,0x15a0,0x15ac,
    0x15b8,0x15c4,0x15d0,0x15dc,0x15ea,0x15f6,0x1602,0x160e,0x1626,
    0x1632,0x163e,0x164a,0x1656,0x1662,0x166e,0x167a,0x1686,0x186c,
    0x187b,0x190f,0x1fd6,0x1fe2}
local function spawn(count)
    local src=source_enemy()
    if not src or slots()<count then return 'sin enemigos compatibles o espacios libres' end
    local x=r(0xe4+src)+256*r(0x14e0+src)
    local y=r(0xd8+src)+256*r(0x14d4+src)
    if math.abs(x-r16(0x94))<64 then return 'enemigo demasiado cerca de Mario' end
    local selected={}
    for s=9,0,-1 do if r(0x14c8+s)==0 and #selected<count then selected[#selected+1]=s end end
    for _,s in ipairs(selected) do
        for _,base in ipairs(sprite_fields) do w(base+s,r(base+src)) end
        -- Retain the original spawn position: proven visible and inside the room.
        w(0xe4+s,x%256); w(0x14e0+s,math.floor(x/256))
        w(0xd8+s,y%256); w(0x14d4+s,math.floor(y/256))
        w(0x161a+s,255); w(0x14c8+s,8)
    end
end
local function extend(id,ms)
    local now=emu.framecount()
    local frames=math.max(1,math.floor(math.min(ms,duration[id]*1000)*60/1000))
    C.active[id]=math.min(now+duration[id]*60,math.max(now,C.active[id] or now)+frames)
end
local function apply(e)
    if not C.playing or C.paused or not C.connected then return 'partida inactiva' end
    if e.epoch~=C.epoch then return 'intento anterior' end
    local supported=false
    for _,id in ipairs(caps()) do if id==e.effect then supported=true end end
    if not supported then return 'no disponible en este nivel' end
    local id=e.effect
    if harmful[id] then
        if C.safe or e.safe then return 'modo sin morir activo' end
        if (C.active.shield or 0)>emu.framecount() then return 'escudo activo' end
    end
    if id=='mushroom' then w(0x19,1)
    elseif id=='fire_flower' then w(0x19,3)
    elseif id=='cape' then w(0x19,2)
    elseif id=='small' then w(0x19,0)
    elseif id=='life_add' then
        if r(0xdbe)>=98 then return 'ya tienes 99 vidas' end
        w(0xdbe,r(0xdbe)+1)
    elseif id=='time_add' then
        if clock()>=999 then return 'reloj al maximo' end
        set_clock(math.min(999,clock()+30))
    elseif id=='time_subtract' then set_clock(math.max(10,clock()-30))
    elseif id=='spawn_enemy' then local reason=spawn(1); if reason then return reason end
    elseif id=='spawn_wave' then local reason=spawn(3); if reason then return reason end
    elseif duration[id] then
        if id=='shield' then
            for bad in pairs(harmful) do C.active[bad]=nil end
        end
        if id=='fire_stream' then game.command('fire_stream','30') end
        extend(id,e.ms)
    else return 'efecto desconocido' end
    return 'ok'
end
function C.poll(session,connected,paused,safe,effects)
    assert(session==C.session,'session mismatch')
    C.connected=connected; C.paused=paused; C.safe=safe; C.lease=emu.framecount()+120
    if safe then for id in pairs(harmful) do C.active[id]=nil end end
    refresh()
    local replies={}
    for _,e in ipairs(effects) do
        local result=C.seen[e.id]
        if not result then
            result=apply(e)
            C.seen[e.id]=result; C.order[#C.order+1]=e.id
            if #C.order>512 then C.seen[table.remove(C.order,1)]=nil end
        end
        replies[#replies+1]=e.id..':'..result
    end
    return C.session..'|'..C.epoch..'|'..(C.playing and '1' or '0')..'|'..table.concat(caps(),',')..'|'..table.concat(replies,';')
end
event.onframestart(function()
    refresh()
    local now=emu.framecount()
    if now>=C.lease then C.connected=false; clear() end
    if not C.playing or C.paused or not C.connected then return end
    for id,until_frame in pairs(C.active) do
        if now>=until_frame then
            C.active[id]=nil
            if id=='fire_stream' then fire_reset() end
            if id=='invincible' then
                if C.invincibleWritten and r(0x1497)<=2 then w(0x1497,0) end
                C.invincibleWritten=false
            end
        end
    end
    if C.active.invincible then
        -- Preserve stronger protection already granted by the native game.
        if r(0x1497)<=2 then w(0x1497,2); C.invincibleWritten=true end
    end
    if C.active.fire_stream and not land() then C.active.fire_stream=nil; fire_reset() end
    local buttons=joypad.get(1)
    if C.active.invert_controls then joypad.set({Left=buttons.Right,Right=buttons.Left},1) end
    if land() then
        if C.active.auto_jump then joypad.set({B=(now%12==0)},1) end
        if C.active.super_speed and r(0x77)&4~=0 then
            local velocity=r(0x7b); if velocity>=128 then velocity=velocity-256 end
            if velocity~=0 then w(0x7b,math.max(-48,math.min(48,velocity+(velocity>0 and 2 or -2)))%256) end
        end
        local vy=r(0x7d)
        if C.active.high_jump and C.grounded and r(0x77)&4==0 and vy>=128 then w(0x7d,0xa8) end
    end
    C.grounded=r(0x77)&4~=0
end,'cloud90.smw')
return 'cloud90-smw-1'
