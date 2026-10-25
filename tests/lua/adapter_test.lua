-- Fake boundary objects exercise our adapter without copying game scripts.
local adapter = dofile("game/PokerNightMultiplayer.lua")
local passed = 0
local function test(name, fn)
  local ok, err = pcall(fn)
  if not ok then error(name .. ": " .. tostring(err)) end
  passed = passed + 1
  print("PASS " .. name)
end
local function rejected(fn) assert(not pcall(fn), "Expected rejection") end
local function container(cards)
  return { GetSize = function() return #cards end, Get = function(_,i)
    local c = cards[i]
    return { GetRank = function() return c[1] end, GetSuit = function() return c[2] end }
  end }
end
local names = { "Max", "Strongbad", "Heavy", "Tycho", "Player" }
local players = {}
for i, name in ipairs(names) do
  local p = { name = name, chips = 1000, contribution = 0, hole = container({{i+2,1},{i+3,2}}) }
  function p:GetName() return self.name end
  function p:GetChips() return self.chips end
  function p:GetChipsInPot() return self.contribution end
  function p:GetHand() return { holeCards = self.hole } end
  function p:IsFolded() return self.folded or false end
  function p:IsBusted() return false end
  function p:IsAllIn() return self.chips == 0 end
  players[i] = p
end
local tableObject = { cardsTurnedOver = false, callValue = 100 }
function tableObject:GetPlayers() return players end
function tableObject:GetCommunityCards() return { cards = container({}) } end
function tableObject:GetPotManager() return { GetAmount = function() return 150 end } end
function tableObject:GetCallValue() return self.callValue end
function tableObject:GetBigBlindValue() return 100 end
function LogicGet(_) return 100 end
Player = { Action = { CALL = 1, RAISE = 2, FOLD = 3 } }
function Player.Call(p) local amount = math.min(p.chips, tableObject.callValue - p.contribution); p.chips = p.chips - amount; p.contribution = p.contribution + amount end
local raiseSeen
function Player.Raise(_, amount, state) raiseSeen = amount end
test("snapshot separates public and hidden cards", function()
  local snapshot = adapter.capture(tableObject, { sessionId="test",handId=1,turnId=1,sequence=1,activeSeat="heavy",street="preflop" })
  assert(snapshot.public.players[3].seat == "heavy")
  assert(snapshot.public.players[3].holeCards == nil and next(snapshot.public.revealedHands) == nil)
  assert(#snapshot.holeCards.heavy == 2 and snapshot.holeCards.heavy[1].rank == 5)
  assert(snapshot.public.toCall == 100 and snapshot.public.minimumRaise == 200)
end)
test("partial deal does not publish an incomplete private hand", function()
  local previous=players[3].hole
  players[3].hole=container({{5,1}})
  local snapshot=adapter.capture(tableObject,{sessionId="test",handId=1,turnId=1,sequence=2,street="dealing"})
  assert(#snapshot.holeCards.heavy==0)
  players[3].hole=previous
end)

test("wire contribution converts to engine raise increment", function()
  adapter.applyAction(tableObject,"heavy",{action="raise",amount=350},{name="ModePreFlop"})
  assert(raiseSeen == 250)
end)
test("illegal actions rejected without a game mutation", function()
  rejected(function() adapter.applyAction(tableObject,"heavy",{action="check"},{name="ModePreFlop"}) end)
  rejected(function() adapter.applyAction(tableObject,"heavy",{action="raise",amount=101},{name="ModePreFlop"}) end)
  rejected(function() adapter.applyAction(tableObject,"heavy",{action="raise",amount=-1},{name="ModePreFlop"}) end)
  rejected(function() adapter.applyAction(tableObject,"heavy",{action="call",amount=1},{name="ModePreFlop"}) end)
end)
test("fold delegates mutation to round driver", function()
  local action = adapter.applyAction(tableObject,"max",{action="fold"},{name="ModePreFlop"})
  assert(action == Player.Action.FOLD and not players[1].folded)
end)
test("all-in below call uses engine call", function()
  players[3].chips = 50
  local action, text = adapter.applyAction(tableObject,"heavy",{action="allIn"},{name="ModePreFlop"})
  assert(action == Player.Action.CALL and text == "allIn" and players[3].chips == 0)
end)
test("normal showdown reveals only the players already shown by the engine", function()
  players[3].showingHandCards=true
  local hidden=adapter.capture(tableObject,{sessionId="test",handId=1,turnId=1,sequence=2,street="preflop"})
  assert(hidden.public.revealedHands.heavy==nil,"Solo spectator UI must not reveal cards in multiplayer")
  local snapshot=adapter.capture(tableObject,{sessionId="test",handId=1,turnId=1,sequence=2,street="showdown"})
  assert(#snapshot.public.revealedHands.heavy==2 and snapshot.public.revealedHands.max==nil)
  players[3].showingHandCards=false
end)

test("opponent folded cards remain hidden at showdown", function()
  tableObject.cardsTurnedOver = true
  players[1].folded = true
  local snapshot = adapter.capture(tableObject, {sessionId="test",handId=1,turnId=1,sequence=2,street="showdown"})
  assert(snapshot.public.revealedHands.max == nil and #snapshot.public.revealedHands.heavy == 2)
end)
print(passed .. " Lua adapter boundary tests passed; no live game validation.")
