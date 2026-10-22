// Runs tests/mod_test.lua with fengari (Lua in JavaScript): npm install, then
// node tests/run.js
const path = require('path');
const { lua, lauxlib, lualib, to_luastring } = require('fengari');

const L = lauxlib.luaL_newstate();
lualib.luaL_openlibs(L);
const mod = path.join(__dirname, '..', 'mod', 'Cloud90.lua');
lua.lua_pushstring(L, to_luastring(mod));
lua.lua_setglobal(L, to_luastring('MOD_PATH'));
const test = path.join(__dirname, 'mod_test.lua');
if (lauxlib.luaL_dofile(L, to_luastring(test)) !== lua.LUA_OK) {
  console.error(lua.lua_tojsstring(L, -1));
  process.exit(1);
}
