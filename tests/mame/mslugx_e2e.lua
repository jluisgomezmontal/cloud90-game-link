-- Native mission, actual ROM tables, shooting/throwing, JSON ACK and lifecycle.
local dir=assert(os.getenv('CLOUD90_DIR'));local session=assert(os.getenv('CLOUD90_SESSION'))
local json=require('json');local log=assert(io.open(dir..'/e2e.log','w'))
local S=assert(require('cloud90').state,'MSX adapter did not start')
local mem=manager.machine.devices[':maincpu'].spaces.program
local frame,since,serial,failures=0,nil,0,0;local batch={};local accepted={};local finished=false
local function check(ok,label)log:write((ok and 'PASS ' or 'FAIL ')..label..'\n');log:flush();if not ok then failures=failures+1 end end
local function press(name,on)for _,p in pairs(manager.machine.ioport.ports) do local f=p.fields[name];if f then f:set_value(on and 1 or 0) end end end
local function inbox(paused,effects)return{session=session,heartbeatUtcMs=os.time()*1000,paused=paused or false,noDeath=true,effects=effects or {}} end
local function writeInbox()local f=assert(io.open(dir..'/inbox.json','w'));f:write(json.stringify(inbox(false,batch)));f:close()end
local function command(id,arg,ms,epoch)
 serial=serial+1;return{id='native_'..serial,effect=id,ms=ms or 1000,arg=arg or '',safe=true,epoch=epoch or S.epoch,expiresUtcMs=os.time()*1000+10000}
end
local function apply(id,arg,ms)
 local e=command(id,arg,ms);S.receive(inbox(false,{e}));check(S.seen[e.id]=='ok',id..' ACK '..tostring(S.seen[e.id]));if S.seen[e.id]=='ok' then accepted[id]=true end;return e
end
local steps={};local step=1
local function at(n,fn)steps[#steps+1]={n,fn}end
at(190,function()check(mem:read_u8(0x10c720)==9,'real grenade button consumes one grenade before modifiers')end)
at(200,function()
 for w=0,15 do log:write(string.format('TABLE %d sub=%d ammo=%d form=%d\n',w,mem:read_u8(0xc08a8+w),mem:read_u16(0xc08b8+w*2),mem:read_u8(0xc0918+w))) end
 check(#S.capabilities()==23,'all 23 native capabilities ready only in mission')
 local before=mem:read_u8(0x10c720);local e=apply('grenades_add');check(mem:read_u8(0x10c720)==math.min(99,before+10),'grenade counter +10')
 S.receive(inbox(false,{e}));check(mem:read_u8(0x10c720)==math.min(99,before+10),'duplicate does not add again')
 apply('life_add');check(mem:read_u8(0x101288)==3,'reserve lives +1');apply('life_remove');check(mem:read_u8(0x101288)==2,'reserve lives -1 without killing soldier')
 apply('grenades_remove');check(mem:read_u8(0x10c720)==0,'empty grenades');apply('grenades_add')
 local t=mem:read_u8(0x1013de);apply('timer_add');check(mem:read_u8(0x1013de)==math.min(99,t+20),'binary timer +20')
 apply('timer_remove');check(mem:read_u8(0x1013de)==t,'binary timer -20')
 apply('invincible',nil,15000)
end)
local weapons={'hmg','shotgun','rocket','flame','laser','chaser','lizard','drop','super_grenade'}
local ammoBefore,grenadesBefore,freezeValue,epochBefore,deathEpoch,deadSeen
for i,name in ipairs(weapons) do
 at(220+(i-1)*50,function()
  apply('give_weapon',name);ammoBefore=mem:read_u16(0x10c724);check(ammoBefore>0 and ammoBefore<999,'native '..name..' finite ammo')
 end)
 at(260+(i-1)*50,function()
  check(mem:read_u16(0x10c724)<ammoBefore,'real '..name..' firing consumes native ammo')
  manager.machine.screens[':screen']:snapshot(dir..'/weapon-'..name..'.png')
 end)
end
at(680,function()
 local ammo=mem:read_u16(0x10c724);apply('ammo_add');check(mem:read_u16(0x10c724)==ammo+100,'ammo +100');apply('remove_weapon');check(mem:read_u8(0x10c71e)==0 and mem:read_u16(0x10c724)==65535,'restore pistol with infinite ammo')
 grenadesBefore=mem:read_u8(0x10c720)
end)
at(740,function()
 apply('timer_freeze',nil,5000);freezeValue=mem:read_u8(0x1013de)
 for _,id in ipairs({'invert_controls','no_jump','no_shoot','no_grenade','paralyze','auto_jump','auto_fire','tunnel_vision','blackout'}) do apply(id) end
 local rate=manager.machine.video.throttle_rate;apply('slow_motion');check(manager.machine.video.throttle_rate==0.5,'slow motion native rate')
 apply('turbo');check(manager.machine.video.throttle_rate==1.25,'turbo replaces slow motion')
 apply('shield');check(manager.machine.video.throttle_rate==rate and not S.active.blackout and not S.active.paralyze,'shield cancels trolls and restores prior speed')
 local g=mem:read_u8(0x10c720);local e=command('grenades_remove');S.receive(inbox(false,{e}));check(S.seen[e.id]=='escudo activo' and mem:read_u8(0x10c720)==g,'shield blocks new sabotage')
end)
at(850,function()
 check(mem:read_u8(0x1013de)==freezeValue,'timer remains frozen across real frames')
 check(mem:read_u16(0x101548)>=118,'native invulnerability holds respawn counter')
 S.receive(inbox(true));check(next(S.active)==nil and mem:read_u16(0x101548)==0,'panic clears temporaries and owned invulnerability')
 local e=command('grenades_add');batch={e};writeInbox()
end)
at(870,function()
 local f=assert(io.open(dir..'/acks.txt'));local text=f:read('a');f:close();check(text:find('ack '..batch[1].id..' ok',1,true)~=nil,'real JSON inbox ACK');batch={}
 local e=command('life_add',nil,nil,'old_attempt');S.receive(inbox(false,{e}));check(S.seen[e.id]=='intento anterior','stale attempt rejected')
 epochBefore=S.epoch;apply('no_shoot',nil,5000);manager.machine:save('msx-test')
end)
at(910,function()manager.machine:load('msx-test')end)
local resumePending=false
at(950,function()check(S.epoch~=epochBefore and not S.active.no_shoot,'real savestate invalidates commands');resumePending=true;emu.pause()end)
emu.register_periodic(function()if resumePending and manager.machine.paused then resumePending=false;check(next(S.active)==nil,'native MAME pause clears effects');emu.unpause()end end)
at(1000,function()
 local count=0;for _ in pairs(accepted) do count=count+1 end;check(count==23,'all 23 modifiers accepted natively')
 check(S.inPlay(),'mission remains playable');deathEpoch=S.epoch;apply('timer_freeze',nil,20000)
end)
MSX_E2E=emu.add_machine_frame_notifier(function()
 if finished then return end
 frame=frame+1
 if frame%300==0 and not since then
  log:write(string.format('WAIT %d game=%d bios=%d/%d ptr=%x status=%d state=%d vehicle=%d form=%d lives=%d timer=%d grenades=%d weapon=%d ammo=%d blocked=%s\n',frame,mem:read_u8(0x100088),mem:read_u8(0x10fdb6),mem:read_u8(0x10fdb7),mem:read_u32(0x101274),mem:read_u8(0x101290),mem:read_u8(0x10151c),mem:read_u8(0x10c718),mem:read_u8(0x101557),mem:read_u8(0x101288),mem:read_u8(0x1013de),mem:read_u8(0x10c720),mem:read_u8(0x10c71e),mem:read_u16(0x10c724),tostring(manager.ui.menu_active)));log:flush()
 end
 press('Coin 1',frame>=600 and frame<610);press('1 Player Start',frame>=720 and frame<730)
 if not since then press('P1 A',frame>800 and frame%30<5) end
 if not since and S.inPlay() then since=frame;press('P1 A',false);check(frame>900,'tutorial and selection rejected before real mission') end
 if since then
  local n=frame-since
  if n%60==0 then log:write(string.format('STATE %d active=%s life=%d st=%d form=%d vehicle=%d inv=%d epoch=%s\n',n,tostring(S.inPlay()),mem:read_u8(0x101288),mem:read_u8(0x10151c),mem:read_u8(0x101557),mem:read_u8(0x10c718),mem:read_u16(0x101548),S.epoch));log:flush()end
  press('P1 A',n>=220 and n<660 and n%12<4)
  press('P1 C',n>=150 and n<162)
  press('P1 Right',n>=1000 and n<1500)
  while steps[step] and n>=steps[step][1] do local ok,err=pcall(steps[step][2]);if not finished then check(ok,'step '..step..(ok and '' or ': '..tostring(err)))end;step=step+1 end
  if deathEpoch and mem:read_u8(0x101288)<2 then deadSeen=true end
  if deadSeen and S.inPlay() then
   check(S.epoch~=deathEpoch and not S.active.timer_freeze,'real enemy death and respawn invalidate epoch and clear effects')
   log:write('RESULT failures='..failures..'\n');finished=true;log:close();manager.machine:exit();return
  end
 end
 if frame%3==0 then writeInbox() end
 if frame>7000 then check(false,'timeout entering mission');log:write('RESULT failures='..failures..'\n');log:close();manager.machine:exit()end
end)
