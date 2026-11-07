-- Metal Slug X / MAME 0.289. Independently implemented; address facts and
-- native evidence are documented in mod/mslugx/README.md. No MS3 address reuse.
local M={}
local controls={'Up','Down','Left','Right','A','B','C','D'}
local timed={invincible=15,timer_freeze=20,invert_controls=10,no_jump=8,no_shoot=6,
 no_grenade=10,paralyze=2,auto_jump=10,auto_fire=10,tunnel_vision=10,blackout=2,
 slow_motion=10,turbo=8,shield=60}
local trolls={life_remove=true,grenades_remove=true,remove_weapon=true,timer_remove=true,
 invert_controls=true,no_jump=true,no_shoot=true,no_grenade=true,paralyze=true,
 auto_jump=true,tunnel_vision=true,blackout=true,slow_motion=true,turbo=true}
local effects={'life_add','life_remove','grenades_add','grenades_remove','give_weapon',
 'remove_weapon','ammo_add','invincible','timer_add','timer_remove','timer_freeze',
 'invert_controls','no_jump','no_shoot','no_grenade','paralyze','auto_jump','auto_fire',
 'tunnel_vision','blackout','slow_motion','turbo','shield'}
local allowed={};for _,id in ipairs(effects) do allowed[id]=true end
local weapons={hmg=5,shotgun=2,rocket=3,flame=4,laser=6,chaser=12,lizard=13,drop=14,super_grenade=15}
local order={'hmg','shotgun','rocket','flame','laser','chaser','lizard','drop','super_grenade'}
local function integer(v,lo,hi)return type(v)=='number' and v%1==0 and v>=lo and v<=hi end

function M.new(env)
 local mem=env.mem
 local S={session=env.session,epoch=env.session..'_0',sequence=0,playing=false,paused=true,
  healthy=false,active={},seen={},order={},frame=0}
 local function release()for _,name in ipairs(controls) do env.fields[name]:clear_value() end;S.touched=false end
 local function restore(id)
  local t=S.active[id]
  if (id=='slow_motion' or id=='turbo') and t and env.video.throttle_rate==t.rate then env.video.throttle_rate=t.previous end
  if id=='invincible' and t and mem:read_u8(0x10151c)==1 and mem:read_u32(0x101274)==0x1014c4 and mem:read_u16(0x101548)>=118 then
   mem:write_u16(0x101548,math.max(0,math.ceil((t.naturalUntil-env.time())*30)))
  end
  S.active[id]=nil
 end
 local function clear()restore('slow_motion');restore('turbo');restore('invincible');S.active={};release() end
 function S.invalidate()clear();S.sequence=S.sequence+1;S.epoch=S.session..'_'..S.sequence;S.playing=false end
 function S.stop()S.invalidate();S.healthy=false;S.paused=true end
 function S.inPlay()
  -- BIOS player flags alone also cover the how-to-play demo. The real status
  -- object's player link and soldier state distinguish it from a live mission.
  local ammo=mem:read_u16(0x10c724)
  return not env.blocked() and mem:read_u8(0x100088)==2
   and mem:read_u8(0x10fdb6)==1 and mem:read_u8(0x10fdb7)==0
   and mem:read_u32(0x101274)==0x1014c4 and mem:read_u8(0x10128c)==1
   and mem:read_u8(0x10151c)==1 and mem:read_u8(0x10c718)<=1
   and mem:read_u8(0x101557)<=1 and integer(mem:read_u8(0x101288),0,9)
   and integer(mem:read_u8(0x1013de),1,99) and mem:read_u8(0x10c720)<=99
   and mem:read_u8(0x10c71e)<=15 and (ammo==65535 or ammo<=999)
 end
 function S.refresh()
  local playing=S.inPlay()
  local zone=mem:read_u16(0x10c2f4)..'_'..mem:read_u16(0x10c2f6)..'_'..mem:read_u16(0x10c2fa)
  if playing~=S.playing or (playing and S.zone~=zone) then
   S.invalidate();if playing then S.graceUntil=env.time()+3 end
  end
  S.playing=playing;S.zone=zone
  if not playing or S.paused or not S.healthy then clear() end
 end
 local function change(addr,delta,lo,hi)
  local n=mem:read_u8(addr);local nextN=math.max(lo,math.min(hi,n+delta))
  if n==nextN then return false,'contador al limite' end
  mem:write_u8(addr,nextN);return true
 end
 local function give(w)
  -- Read subtype, ammo and requested form from this audited ROM's own pickup tables.
  local sub=mem:read_u8(0xc08a8+w);local ammo=mem:read_u16(0xc08b8+w*2);local form=mem:read_u8(0xc0918+w)
  if (sub>15 and sub~=255) or (form>1 and form~=255) or (ammo~=65535 and not integer(ammo,1,999)) then return false,'tabla de arma incompatible' end
  mem:write_u8(0x10c71e,w);mem:write_u8(0x10c71c,sub);mem:write_u8(0x10c71a,0)
  mem:write_u16(0x10c724,ammo);mem:write_u8(0x101558,form);mem:write_u8(0x101559,0)
  return true
 end
 local function extend(id,ms)
  local now=env.time();local limit=timed[id];local old=S.active[id]
  if not old then
   if id=='slow_motion' then restore('turbo') elseif id=='turbo' then restore('slow_motion') end
   old={};S.active[id]=old
   if id=='invincible' then old.naturalUntil=now+math.min(120,mem:read_u16(0x101548))/30 end
   if id=='timer_freeze' then old.clock=mem:read_u8(0x1013de) end
   if id=='slow_motion' or id=='turbo' then old.previous=env.video.throttle_rate;old.rate=id=='turbo' and 1.25 or 0.5;env.video.throttle_rate=old.rate end
  end
  old.untilT=math.min(now+limit,math.max(now,old.untilT or now)+math.max(0.001,math.min(ms/1000,limit)))
 end
 local function apply(e,now)
  if not allowed[e.effect] then return false,'efecto desconocido' end
  if not S.healthy or S.paused then return false,'efectos en pausa' end
  if not S.playing then return false,'no hay soldado compatible activo' end
  if e.epoch~=S.epoch then return false,'intento anterior' end
  if not integer(e.expiresUtcMs,now-10000,now+11000) or e.expiresUtcMs<=now then return false,'comando vencido' end
  if not integer(e.ms,0,120000) or type(e.safe)~='boolean' then return false,'comando invalido' end
  local id=e.effect
  if trolls[id] and S.active.shield then return false,'escudo activo' end
  if trolls[id] and env.time()<(S.graceUntil or 0) then return false,'inicio de intento' end
  if id=='life_add' then return change(0x101288,1,0,9)
  elseif id=='life_remove' then return change(0x101288,-1,0,9)
  elseif id=='grenades_add' then return change(0x10c720,10,0,99)
  elseif id=='grenades_remove' then return change(0x10c720,-99,0,99)
  elseif id=='give_weapon' then
   if e.arg~='' and e.arg~=nil and not weapons[e.arg] then return false,'arma desconocida' end
   return give(weapons[e.arg] or weapons[order[math.random(#order)]])
  elseif id=='remove_weapon' then
   if mem:read_u8(0x10c71e)==0 then return false,'ya tienes pistola' end;return give(0)
  elseif id=='ammo_add' then
   local ammo=mem:read_u16(0x10c724)
   if ammo==65535 or ammo>=999 or mem:read_u8(0x10c71e)==0 then return false,'municion no disponible' end
   mem:write_u16(0x10c724,math.min(999,ammo+100))
  elseif id=='timer_add' or id=='timer_remove' then
   if id=='timer_remove' and mem:read_u8(0x1013de)<=10 then return false,'contador al limite' end
   local ok,reason=change(0x1013de,id=='timer_add' and 20 or -20,10,99)
   if S.active.timer_freeze then S.active.timer_freeze.clock=mem:read_u8(0x1013de) end
   return ok,reason
  elseif timed[id] then
   if id=='shield' then for bad in pairs(trolls) do restore(bad) end;release()
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
  S.lastHeartbeat=inbox.heartbeatUtcMs;S.healthy=true;S.paused=inbox.paused;S.refresh()
  for _,e in ipairs(inbox.effects) do
   if type(e)=='table' and type(e.id)=='string' and #e.id<=80 and e.id:match('^[A-Za-z0-9_-]+$') then
    local result=S.seen[e.id]
    if not result then
     local good,ok,reason=pcall(apply,e,now);result=good and ok and 'ok' or (good and reason or 'error del adaptador')
     S.seen[e.id]=result;S.order[#S.order+1]=e.id;if #S.order>512 then S.seen[table.remove(S.order,1)]=nil end
    end
    env.ack(e.id,result)
   end
  end
 end
 function S.capabilities()return S.playing and S.healthy and effects or {} end
 function S.tick()
  S.frame=S.frame+1;S.refresh()
  if not S.lastHeartbeat or math.abs(env.wall()-S.lastHeartbeat)>4000 then S.healthy=false;clear() end
  if not S.playing or S.paused or not S.healthy then return end
  for id,t in pairs(S.active) do if env.time()>=t.untilT then restore(id) end end
  if S.active.timer_freeze then mem:write_u8(0x1013de,S.active.timer_freeze.clock) end
  if S.active.invincible then mem:write_u16(0x101548,120) end
  local wants={};local any=false
  for _,id in ipairs({'invert_controls','no_jump','no_shoot','no_grenade','paralyze','auto_jump','auto_fire'}) do if S.active[id] then any=true end end
  if not any then if S.touched then release() end;return end
  for _,name in ipairs(controls) do wants[name]=env.real(name) end
  if S.active.invert_controls then wants.Left,wants.Right=wants.Right,wants.Left end
  if S.active.auto_jump then wants.B=wants.B or S.frame%40<4 end
  if S.active.auto_fire then wants.A=wants.A or S.frame%8<3 end
  if S.active.no_jump then wants.B=false end
  if S.active.no_shoot then wants.A=false end
  if S.active.no_grenade then wants.C=false end
  if S.active.paralyze then for _,name in ipairs(controls) do wants[name]=false end end
  for _,name in ipairs(controls) do env.fields[name]:set_value(wants[name] and 1 or 0) end;S.touched=true
 end
 function S.draw()
  local screen=env.screen;if not screen then return end;local w,h=screen.width,screen.height
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
 require('cloud90/session').start(owner,{id='mslugx',system='mslugx',controls=controls,create=M.new})
end
return M
