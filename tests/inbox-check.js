// Loads an inbox file with real Lua (fengari) and checks the hostile viewer
// name came through as the same string (tests/link-test.ps1).
const fs = require('fs');
const { lua, lauxlib, lualib, to_luastring, to_jsstring } = require('fengari');

const [file, expected] = process.argv.slice(2);
const L = lauxlib.luaL_newstate();
lualib.luaL_openlibs(L);
if (lauxlib.luaL_loadstring(L, to_luastring(fs.readFileSync(file, 'utf8'))) !== lua.LUA_OK || lua.lua_pcall(L, 0, 1, 0) !== lua.LUA_OK) {
  console.log('FAIL inbox is not valid Lua: ' + lua.lua_tojsstring(L, -1));
  process.exit(1);
}
lua.lua_getfield(L, -1, to_luastring('effects'));
lua.lua_rawgeti(L, -1, 1);
lua.lua_getfield(L, -1, to_luastring('by'));
const by = to_jsstring(lua.lua_tolstring(L, -1));
lua.lua_pop(L, 1);
lua.lua_getfield(L, -1, to_luastring('safe'));
const safe = lua.lua_toboolean(L, -1);
const want = Buffer.from(expected, 'base64').toString('utf8');
if (by !== want || !safe) {
  console.log('FAIL by=' + JSON.stringify(by) + ' want=' + JSON.stringify(want) + ' safe=' + safe);
  process.exit(1);
}
console.log('inbox ok');
