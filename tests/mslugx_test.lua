local M=dofile(MOD_PATH)
local ram,fields,physical,acks={},{},{},{}
local now,wall,blocked=0,1700000000000.0,false
local mem={read_u8=function(_,a)return ram[a] or 0 end,write_u8=function(_,a,v)ram[a]=v end}
function mem:read_u16(a)return self:read_u8(a)*256+self:read_u8(a+1)end
function mem:write_u16(a,v)self:write_u8(a,v//256);self:write_u8(a+1,v%256)end
function mem:read_u32(a)return self:read_u16(a)*65536+self:read_u16(a+2)end
function mem:write_u32(a,v)self:write_u16(a,v//65536);self:write_u16(a+2,v%65536)end
for _,n in ipairs({'Up','Down','Left','Right','A','B','C','D'})do fields[n]={set_value=function(self,v)self.value=v end,clear_value=function(self)self.value=nil end}end
local video={throttle_rate=1.1};local drawings=0
local screen={width=320,height=224,draw_box=function()drawings=drawings+1 end,draw_text=function()end}
local S=M.new({session='abc123',mem=mem,fields=fields,video=video,screen=screen,time=function()return now end,wall=function()return wall end,blocked=function()return blocked end,real=function(n)return physical[n] or false end,ack=function(id,r)acks[id]=r end})
local serial=0
local function envelope(es,paused)S.receive({session='abc123',heartbeatUtcMs=wall,paused=paused or false,noDeath=true,effects=es or {}})end
local function cmd(id,arg,ms)serial=serial+1;return{id='e_'..serial,effect=id,arg=arg or '',ms=ms or 1000,safe=true,epoch=S.epoch,expiresUtcMs=wall+10000}end
local function apply(e,expected)if type(e)=='string'then e=cmd(e)end;envelope({e});assert(acks[e.id]==(expected or 'ok'),e.effect..': '..tostring(acks[e.id]));return e end
local function tick(n)for _=1,n do now=now+1/60;wall=wall+17;envelope();S.tick()end end
local function ready()
 mem:write_u8(0x100088,2);mem:write_u8(0x10fdb6,1);mem:write_u8(0x10fdb7,0);mem:write_u32(0x101274,0x1014c4)
 mem:write_u8(0x10128c,1);mem:write_u8(0x10151c,1);mem:write_u8(0x10c718,0);mem:write_u8(0x101557,0)
 mem:write_u8(0x101288,2);mem:write_u8(0x1013de,60);mem:write_u8(0x10c720,10);mem:write_u8(0x10c71e,0);mem:write_u16(0x10c724,65535)
 for w=0,15 do mem:write_u8(0xc08a8+w,w==5 and 255 or 1);mem:write_u16(0xc08b8+w*2,w==0 and 65535 or 200);mem:write_u8(0xc0918+w,w==0 and 0 or 1)end
 envelope();tick(181)
end
envelope();assert(#S.capabilities()==0);ready();assert(#S.capabilities()==23)
local e=apply('grenades_add');assert(mem:read_u8(0x10c720)==20);apply(e);assert(mem:read_u8(0x10c720)==20)
mem:write_u8(0x10c720,95);apply('grenades_add');assert(mem:read_u8(0x10c720)==99);apply('grenades_add','contador al limite');apply('grenades_remove');assert(mem:read_u8(0x10c720)==0)
apply('life_add');assert(mem:read_u8(0x101288)==3);apply('life_remove');assert(mem:read_u8(0x101288)==2);mem:write_u8(0x101288,0);apply('life_remove','contador al limite');mem:write_u8(0x101288,9);apply('life_add','contador al limite')
apply(cmd('give_weapon','hmg'));assert(mem:read_u8(0x10c71e)==5 and mem:read_u8(0x10c71c)==255 and mem:read_u16(0x10c724)==200)
apply('ammo_add');assert(mem:read_u16(0x10c724)==300);apply('remove_weapon');assert(mem:read_u8(0x10c71e)==0 and mem:read_u16(0x10c724)==65535);apply('ammo_add','municion no disponible');apply(cmd('give_weapon','bad'),'arma desconocida')
mem:write_u8(0x1013de,95);apply('timer_add');assert(mem:read_u8(0x1013de)==99);mem:write_u8(0x1013de,12);apply('timer_remove');assert(mem:read_u8(0x1013de)==10);apply('timer_remove','contador al limite');apply('timer_freeze');mem:write_u8(0x1013de,9);S.tick();assert(mem:read_u8(0x1013de)==10)
envelope({},true);assert(next(S.active)==nil);envelope();mem:write_u8(0x1013de,5);apply('timer_remove','contador al limite');assert(mem:read_u8(0x1013de)==5);mem:write_u8(0x1013de,60)
physical.Left=true;physical.A=true;physical.B=true;physical.C=true
apply('invert_controls');S.tick();assert(fields.Left.value==0 and fields.Right.value==1)
apply('no_jump');apply('no_shoot');apply('no_grenade');S.tick();assert(fields.A.value==0 and fields.B.value==0 and fields.C.value==0)
apply('paralyze');S.tick();for _,f in pairs(fields)do assert(f.value==0)end
apply('shield');S.tick();assert(not S.active.paralyze and fields.A.value==nil);apply('no_shoot','escudo activo');envelope({},true);envelope()
physical={};apply('auto_fire');apply('auto_jump');S.frame=0;S.tick();assert(fields.A.value==1 and fields.B.value==1);tick(61);assert(fields.A.value==nil)
apply('blackout');S.draw();assert(drawings==1);apply('tunnel_vision');S.draw();assert(drawings==5 and not S.active.blackout)
apply('slow_motion');assert(video.throttle_rate==0.5);apply('turbo');assert(video.throttle_rate==1.25 and not S.active.slow_motion);apply('shield');assert(video.throttle_rate==1.1)
envelope({},true);envelope();mem:write_u16(0x101548,0);apply('invincible');S.tick();assert(mem:read_u16(0x101548)==120);envelope({},true);assert(mem:read_u16(0x101548)==0)
envelope();apply('invincible');S.tick();blocked=true;S.tick();assert(mem:read_u16(0x101548)==0);blocked=false;ready()
local stale=cmd('grenades_add');S.invalidate();envelope();apply(stale,'intento anterior');tick(181)
local expired=cmd('life_add');expired.expiresUtcMs=wall-1;apply(expired,'comando vencido')
local old=S.epoch;mem:write_u16(0x10c2f6,1);S.tick();assert(S.epoch~=old and next(S.active)==nil);tick(181)
apply('no_shoot');mem:write_u8(0x10151c,5);S.tick();assert(not S.playing and next(S.active)==nil);ready()
mem:write_u8(0x10c718,2);S.tick();assert(#S.capabilities()==0);ready();mem:write_u8(0x101557,3);S.tick();assert(#S.capabilities()==0);ready()
mem:write_u8(0x10fdb7,1);S.tick();assert(not S.playing);ready();mem:write_u32(0x101274,0);S.tick();assert(not S.playing);ready()
blocked=true;S.tick();assert(not S.playing);blocked=false;ready();apply('slow_motion');wall=wall+5000;S.tick();assert(not S.healthy and next(S.active)==nil and video.throttle_rate==1.1)
ready();apply('auto_fire');S.receive({session='wrong'});assert(not S.healthy and fields.A.value==nil)
print('MSX: mission/demo gates, all 23 effects, native pickup fields, caps, limits, dedupe, epochs, expiry, input, shield, speed and invulnerability restoration passed')
