local json=dofile("game/Json.lua")
local originalDofile,originalOpen=dofile,io.open
local passed=0
local function run(name,config,ended,blocked)
  local saves,prefs=0,0
  local savedSave,savedPrefs=Save,SavePrefs
  Save=function() saves=saves+1 end
  SavePrefs=function() prefs=prefs+1 end
  io.open=function(path)
    local data=path:match("session%-config.json$") and config or path:match("end.json$") and ended
    if not data then return nil end
    return {read=function() return json.encode(data) end,close=function() end}
  end
  dofile=function(path)
    if path=="PNM_InstallConfig.lua" then return {directory="test"} end
    if path=="PNM_Json.lua" then return json end
    return originalDofile(path)
  end
  local ok,err=pcall(function()
    originalDofile("game/BootGuard.lua")
    Save("persistent.prop");Save("logical:<User>\\PERSISTENT.PROP");SavePrefs()
    assert(saves==(blocked and 0 or 2) and prefs==(blocked and 0 or 1),"Unexpected solo persistence behavior")
    Save("other_resource.prop");Save({})
    assert(saves==(blocked and 2 or 4),"Unrelated resources must retain their save behavior")
  end)
  dofile,io.open,Save,SavePrefs=originalDofile,originalOpen,savedSave,savedPrefs
  assert(ok,name .. ": " .. tostring(err));passed=passed+1;print("PASS " .. name)
end
run("active multiplayer blocks startup solo synchronization",{role="host",sessionId="active"},nil,true)
run("ordinary solo startup keeps native persistence",nil,nil,false)
run("ended multiplayer session keeps native solo persistence",{role="client",sessionId="ended"},{sessionId="ended"},false)
print(passed .. " bootstrap persistence tests passed.")
