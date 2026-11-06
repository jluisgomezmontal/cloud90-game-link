local ram, callbacks, frame, rate, inputs = {}, {}, 0, 0, {}
mainmemory={read_u8=function(a) return ram[a] or 0 end,write_u8=function(a,v) assert(v>=0 and v<256); ram[a]=v end}
mainmemory.read_u16_le=function(a) return mainmemory.read_u8(a)+256*mainmemory.read_u8(a+1) end
event={unregisterbyname=function(n) callbacks[n]=nil end,onframestart=function(f,n) callbacks[n]=f end}
game={command=function(n,v) if n=='fire_stream' then rate=tonumber(v) else rate=0 end; return 'ok' end}
emu={framecount=function() return frame end}
joypad={get=function() return inputs end,set=function(v) for k,x in pairs(v) do inputs[k]=x end end}
CLOUD90_SMW_SESSION='abc123'
-- Match the actual sandbox: protected calls are deliberately unavailable.
pcall=nil; xpcall=nil
dofile(MOD_PATH)
local c=cloud90_smw
local serial=0
local function poll(e, paused, safe)
    return c.poll(CLOUD90_SMW_SESSION,true,paused or false,safe or false,e or {})
end
local function effect(id, ms, epoch)
    serial=serial+1
    return {id='test_'..serial,effect=id,ms=ms or 5000,epoch=epoch or c.epoch,safe=false}
end
local function ok(id,ms) local out=poll({effect(id,ms)}); assert(out:find(':ok',1,true),out); return out end
local function fail(e,why) local out=poll({e}); assert(out:find(why,1,true),out) end
local function tick(n)
    for _=1,n do frame=frame+1; ram[0x13]=frame%256; inputs={}; callbacks['cloud90.smw']() end
end
poll(); fail(effect('mushroom'),'partida inactiva')
ram[0x100]=0x14; ram[0x77]=4; poll()
ok('mushroom'); assert(ram[0x19]==1)
ok('cape'); assert(ram[0x19]==2)
ok('fire_flower'); assert(ram[0x19]==3)
ok('small'); assert(ram[0x19]==0)
local duplicate=effect('life_add'); poll({duplicate}); poll({duplicate}); assert(ram[0xdbe]==1)
ram[0xdbe]=98; fail(effect('life_add'),'99 vidas')
ram[0xf31]=3; ram[0xf32]=0; ram[0xf33]=0
ok('time_add'); assert(ram[0xf32]==3)
ok('time_subtract'); assert(ram[0xf32]==0)
ram[0xf31]=0; ram[0xf32]=2; ok('time_subtract'); assert(ram[0xf32]==1 and ram[0xf33]==0)
fail(effect('time_subtract'),'no disponible')
ram[0xf32]=0; fail(effect('time_add'),'no disponible')
fail(effect('small',nil,'old_attempt'),'intento anterior')
local protected=poll({effect('small')},false,true); assert(protected:find('modo sin morir',1,true))
ok('shield'); fail(effect('small'),'escudo activo'); tick(1); poll({},true); assert(next(c.active)==nil)
ok('invincible',999999); tick(1); assert(ram[0x1497]==2); assert(c.active.invincible<=frame+1800)
poll({},true); assert(ram[0x1497]==0)
ok('invincible',1000); tick(60); poll(); tick(2); assert(not c.active.invincible and ram[0x1497]==0)
ok('fire_stream'); assert(rate==30); poll({},true); assert(rate==0)
ram[0x75]=1; fail(effect('fire_stream'),'no disponible'); ram[0x75]=0
ok('invert_controls'); inputs={Left=true,Right=false}; callbacks['cloud90.smw'](); assert(inputs.Right and not inputs.Left)
ok('auto_jump'); ok('super_speed'); ram[0x7b]=47; tick(1); assert(ram[0x7b]==48)
ok('high_jump'); ram[0x77]=0; ram[0x7d]=200; tick(1); assert(ram[0x7d]==0xa8)
ok('fire_stream'); tick(125); assert(not c.connected and rate==0 and next(c.active)==nil)
poll(); local previous=c.epoch; ram[0x100]=0x0e; tick(1); assert(c.epoch~=previous)
ram[0x100]=0x14; poll(); fail(effect('cape',1,previous),'intento anterior')
ram[0xdb2]=1; poll(); fail(effect('cape'),'partida inactiva'); ram[0xdb2]=0; poll()
fail(effect('spawn_enemy'),'no disponible')
ram[0x14c8]=8; ram[0x9e]=4; ram[0xe4]=160; ram[0xd8]=100
ok('spawn_wave'); local count=0; for s=0,9 do if ram[0x14c8+s]==8 then count=count+1; assert(ram[0x9e+s]==4) end end; assert(count==4)
for s=0,9 do ram[0x14c8+s]=8 end
fail(effect('spawn_enemy'),'no disponible')
ram[0x1497]=100; poll({},true); assert(ram[0x1497]==100,'legitimate protection retained')
print('SMW: powers, lives, timer, deduplication, safety, shield, durations, input, speed, jump, watchdog, transitions, co-op and bounded enemies passed')
