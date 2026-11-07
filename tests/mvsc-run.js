const path=require('path');
const {lua,lauxlib,lualib,to_luastring}=require('fengari');
const L=lauxlib.luaL_newstate();lualib.luaL_openlibs(L);
lua.lua_pushstring(L,to_luastring(path.join(__dirname,'..','mame','plugins','cloud90','mvsc.lua')));
lua.lua_setglobal(L,to_luastring('MOD_PATH'));
if(lauxlib.luaL_dofile(L,to_luastring(path.join(__dirname,'mvsc_test.lua')))!==lua.LUA_OK){console.error(lua.lua_tojsstring(L,-1));process.exit(1);}
