-- Small bounded JSON codec for the local typed bridge. No code evaluation.
local M = {}
M.null = setmetatable({}, { __tostring = function() return "null" end })
local arrayMeta = { __jsonArray = true }
function M.array(value) return setmetatable(value or {}, arrayMeta) end
local escapes = { ['"']='\\"', ['\\']='\\\\', ['\b']='\\b', ['\f']='\\f', ['\n']='\\n', ['\r']='\\r', ['\t']='\\t' }
local function quote(value)
  return '"' .. value:gsub('[%z\1-\31\\"]', function(c) return escapes[c] or string.format('\\u%04x', string.byte(c)) end) .. '"'
end
local function encode(value, depth)
  assert(depth <= 32, "JSON depth limit")
  if value == M.null or value == nil then return "null" end
  local kind = type(value)
  if kind == "string" then return quote(value) end
  if kind == "boolean" then return value and "true" or "false" end
  if kind == "number" then assert(value == value and math.abs(value) < math.huge, "Non-finite number"); return string.format("%.14g",value) end
  assert(kind == "table", "Unsupported JSON type")
  local result = {}
  if getmetatable(value) == arrayMeta or #value > 0 then
    for i=1,#value do result[i] = encode(value[i],depth+1) end
    return "[" .. table.concat(result,",") .. "]"
  end
  for key,item in pairs(value) do
    assert(type(key)=="string", "JSON object key must be a string")
    result[#result+1] = quote(key) .. ":" .. encode(item,depth+1)
  end
  table.sort(result)
  return "{" .. table.concat(result,",") .. "}"
end
function M.encode(value) return encode(value,0) end

function M.decode(text)
  assert(type(text)=="string" and #text <= 65536,"JSON size limit")
  local at, length = 1, #text
  local function whitespace() local _,last=text:find("^[ \t\r\n]*",at); at=(last or at-1)+1 end
  local function utf8(code)
    if code<128 then return string.char(code) end
    if code<2048 then return string.char(192+math.floor(code/64),128+code%64) end
    if code<65536 then return string.char(224+math.floor(code/4096),128+math.floor(code/64)%64,128+code%64) end
    return string.char(240+math.floor(code/262144),128+math.floor(code/4096)%64,128+math.floor(code/64)%64,128+code%64)
  end
  local function stringValue()
    assert(text:sub(at,at)=='"',"Expected string"); at=at+1
    local out={}
    while at<=length do
      local c=text:sub(at,at); at=at+1
      if c=='"' then return table.concat(out) end
      if c=='\\' then
        local e=text:sub(at,at); at=at+1
        local simple={['"']='"',['\\']='\\',['/']='/',b='\b',f='\f',n='\n',r='\r',t='\t'}
        if simple[e] then out[#out+1]=simple[e]
        else
          assert(e=='u',"Invalid escape")
          local hex=text:sub(at,at+3); assert(#hex==4 and hex:match('^%x%x%x%x$'),"Invalid unicode")
          local code=tonumber(hex,16); at=at+4
          if code>=55296 and code<=56319 then
            assert(text:sub(at,at+1)=='\\u',"Missing low surrogate"); at=at+2
            local lowText=text:sub(at,at+3); assert(lowText:match('^%x%x%x%x$'),"Invalid surrogate")
            local low=tonumber(lowText,16); assert(low>=56320 and low<=57343,"Invalid low surrogate"); at=at+4
            code=65536+(code-55296)*1024+low-56320
          else assert(code<56320 or code>57343,"Unpaired surrogate") end
          out[#out+1]=utf8(code)
        end
      else assert(string.byte(c)>=32,"Control character"); out[#out+1]=c end
    end
    error("Unterminated string")
  end
  local value
  value=function(depth)
    assert(depth<=32,"JSON depth limit"); whitespace()
    local c=text:sub(at,at)
    if c=='"' then return stringValue() end
    if c=='{' or c=='[' then
      local isArray=c=='['; local result=isArray and M.array() or {}; at=at+1; whitespace()
      local close=isArray and ']' or '}'
      if text:sub(at,at)==close then at=at+1; return result end
      while true do
        whitespace(); local key
        if not isArray then key=stringValue(); whitespace(); assert(text:sub(at,at)==':',"Expected colon");at=at+1;assert(result[key]==nil,"Duplicate JSON key") end
        local item=value(depth+1)
        if isArray then result[#result+1]=item else result[key]=item end
        whitespace(); local delimiter=text:sub(at,at);at=at+1
        if delimiter==close then return result end
        assert(delimiter==',',"Expected separator")
      end
    end
    for word,result in pairs({['true']=true,['false']=false,['null']=M.null}) do
      if text:sub(at,at+#word-1)==word then at=at+#word;return result end
    end
    local start=at
    if c=='-' then at=at+1 end
    if text:sub(at,at)=='0' then at=at+1
    else assert(text:sub(at,at):match('[1-9]'),"Expected number");repeat at=at+1 until not text:sub(at,at):match('%d') end
    if text:sub(at,at)=='.' then at=at+1;assert(text:sub(at,at):match('%d'),"Expected fraction");repeat at=at+1 until not text:sub(at,at):match('%d') end
    if text:sub(at,at):match('[eE]') then
      at=at+1;if text:sub(at,at):match('[+-]') then at=at+1 end
      assert(text:sub(at,at):match('%d'),"Expected exponent");repeat at=at+1 until not text:sub(at,at):match('%d')
    end
    local result=tonumber(text:sub(start,at-1));assert(result and result==result and math.abs(result)<math.huge,"Invalid number");return result
  end
  local result=value(0);whitespace();assert(at>length,"Trailing JSON data");return result
end
return M
