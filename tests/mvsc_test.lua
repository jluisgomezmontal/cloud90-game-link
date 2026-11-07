local M=dofile(MOD_PATH)
local ram,fields,physical,acks={},{},{},{}
local now,wall,blocked=0,1700000000000.0,false
local mem={read_u8=function(_,a)return ram[a] or 0 end,write_u8=function(_,a,v)ram[a]=v end}
function mem:read_u16(a)return self:read_u8(a)*256+self:read_u8(a+1)end
function mem:write_u16(a,v)self:write_u8(a,v//256);self:write_u8(a+1,v%256)end
function mem:read_u32(a)return self:read_u16(a)*65536+self:read_u16(a+2)end
function mem:write_u32(a,v)self:write_u16(a,v//65536);self:write_u16(a+2,v%65536)end
for _,name in ipairs({'Left','Right','Up','Down','Button 1','Button 2','Button 3','Button 4','Button 5','Button 6'}) do
    fields[name]={set_value=function(self,v)self.value=v end,clear_value=function(self)self.value=nil end}
end
local video={throttle_rate=1.25}
local S=M.new({session='abc123',mem=mem,fields=fields,video=video,time=function()return now end,
    wall=function()return wall end,blocked=function()return blocked end,real=function(n)return physical[n] or false end,
    ack=function(id,result)acks[id]=result end})
local serial=0
local function envelope(e,paused,session,heartbeat)
    S.receive({session=session or 'abc123',heartbeatUtcMs=heartbeat or wall,paused=paused or false,noDeath=true,effects=e or {}})
end
local function e(id,ms,epoch)
    serial=serial+1;return{id='test_'..serial,effect=id,ms=ms or 1000,epoch=epoch or S.epoch,safe=true,expiresUtcMs=wall+10000}
end
local function apply(effect,expected)
    local v=type(effect)=='table' and effect or e(effect)
    envelope({v});assert(acks[v.id]==(expected or 'ok'),v.effect..': '..tostring(acks[v.id])..' healthy='..tostring(S.healthy)..' id='..v.id);return v
end
local function tick(n)
    for _=1,n do now=now+1/60;wall=wall+17;envelope();S.tick() end
end
local function fight()
    ram[0xff4100]=1;ram[0xff4101]=1;mem:write_u16(0xff4104,8);mem:write_u16(0xff4106,4);ram[0xff4000]=8;ram[0xff4008]=0x80
    mem:write_u32(0xff40c8,0xff3000);mem:write_u32(0xff40d0,0xff3400)
    for _,p in ipairs({0xff3000,0xff3400,0xff3800,0xff3c00}) do mem:write_u16(p+0x270,100);mem:write_u16(p+0x27a,120) end
    envelope();tick(181)
end
apply('heal','no hay combate individual activo')
fight();apply('heal');assert(mem:read_u16(0xff3270)==136 and mem:read_u16(0xff327a)==144)
local duplicate=apply('damage');local hp=mem:read_u16(0xff3270);apply(duplicate);assert(mem:read_u16(0xff3270)==hp)
mem:write_u16(0xff3270,5);apply('damage');assert(mem:read_u16(0xff3270)==1)
mem:write_u32(0xff40c8,0xff3800);ram[0xff3281]=1
apply('heal');assert(mem:read_u16(0xff3a70)==136 and mem:read_u16(0xff3270)==1,'only the active changes')
apply('rival_heal');assert(mem:read_u16(0xff3670)==136)
apply('power_add');assert(ram[0xff3a74]==1 and ram[0xff3274]==1)
apply('power_empty');assert(mem:read_u16(0xff3a72)==0 and ram[0xff3274]==0)
apply('rival_power');assert(ram[0xff3674]==1 and ram[0xff3e74]==1)
apply('timer_add');assert(ram[0xff4008]==0x99)
apply('timer_remove');assert(ram[0xff4008]==0x79)
ram[0xff4008]=0x12;apply('timer_remove');assert(ram[0xff4008]==0x10)
apply('timer_remove','reloj al minimo')
apply('timer_freeze');ram[0xff4008]=0x09;S.tick();assert(ram[0xff4008]==0x10)
apply('timer_add');ram[0xff4008]=0x29;S.tick();assert(ram[0xff4008]==0x30,'freeze must retain intentional clock changes')
apply('invert_controls');physical.Left=true;S.tick();assert(fields.Right.value==1 and fields.Left.value==0)
physical['Button 1']=true;physical['Button 4']=true;apply('no_punch');S.tick();assert(fields['Button 1'].value==0 and fields['Button 4'].value==1)
apply('no_kick');S.tick();assert(fields['Button 4'].value==0)
physical.Up=true;apply('no_jump');apply('auto_jump');S.tick();assert(fields.Up.value==0,'no-jump takes precedence')
apply('tunnel_vision');apply('blackout');assert(S.active.blackout and not S.active.tunnel_vision)
apply('slow_motion');assert(video.throttle_rate==0.5)
apply('shield');assert(video.throttle_rate==1.25 and not S.active.no_punch and not S.active.blackout)
assert(fields['Button 1'].value==nil);apply('damage','escudo activo')
envelope({},true);assert(next(S.active)==nil)
local old=S.epoch;ram[0xff4000]=0x0c;envelope();assert(not S.playing and S.epoch~=old)
ram[0xff4000]=8;envelope();tick(181);apply(e('heal',1000,old),'combate anterior')
local expired=e('heal');expired.expiresUtcMs=wall-1;apply(expired,'comando vencido')
local invalid=e('no_punch');invalid.ms='oops';apply(invalid,'comando invalido')
apply('nonexistent','efecto desconocido')
apply('slow_motion');envelope({},false,'wrong-session');assert(not S.healthy and video.throttle_rate==1.25)
envelope();apply('slow_motion');wall=wall+5000;S.tick();assert(not S.healthy and next(S.active)==nil and video.throttle_rate==1.25)
envelope();apply('blackout');S.invalidate();assert(not S.playing and next(S.active)==nil,'post-load/reset/pause invalidation')
ram[0xff4101]=3;envelope();assert(not S.playing,'two-player and demo fights blocked')
ram[0xff4101]=1;ram[0xff4100]=0;envelope();assert(not S.playing)
ram[0xff4100]=1;blocked=true;envelope();assert(not S.playing)
blocked=false;mem:write_u32(0xff40c8,0x123456);envelope();assert(not S.playing,'invalid point pointer rejected')
print('MVSC: active/reserve health, recoverable HP, team meter, timer, all controls, screen, speed restoration, shield, epochs, expiry, deduplication, pause and watchdog passed')
