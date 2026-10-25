-- Runs before the original system loads PersistentData.lua and its startup SyncData.
local installation=dofile("PNM_InstallConfig.lua")
local json=dofile("PNM_Json.lua")
local function read(name)
  local file=io.open(installation.directory .. "/" .. name,"rb");if not file then return nil end
  local text=file:read(65537);file:close();if not text or #text>65536 then return nil end
  local ok,value=pcall(json.decode,text);if ok then return value end
end
local config=read("session-config.json")
if not config or (config.role~="host" and config.role~="client") then return end
local ended=read("end.json")
if ended and ended.sessionId==config.sessionId then return end
local save=Save
if type(save)=="function" then
  Save=function(resource,...)
    if type(resource)=="string" then
      local name=resource:lower():gsub("\\","/")
      if name=="persistent.prop" or name:sub(-16)=="/persistent.prop" then return end
    end
    return save(resource,...)
  end
end
if type(SavePrefs)=="function" then SavePrefs=function() end end
PNM_BootGuardActive=true
