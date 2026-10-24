-- Own adapter code for the inspected Telltale Lua 5.2 API.
-- Not automatically installed: engine-thread bootstrap and replica integration are still unverified.
local M = {}
local names = { "Max", "Strongbad", "Heavy", "Tycho", "Player" }
local seats = { "max", "strongBad", "heavy", "tycho", "player" }
local indexBySeat = {}
for i, seat in ipairs(seats) do indexBySeat[seat] = i end

local function copyCards(container)
  local result = {}
  if not container then return result end
  for i = 1, container:GetSize() do
    local card = container:Get(i)
    result[#result + 1] = { rank = card:GetRank(), suit = card:GetSuit() }
  end
  return result
end

function M.capture(gameTable, context)
  assert(context and context.sessionId and context.handId and context.turnId and context.sequence)
  local public = {
    sessionId = context.sessionId, handId = context.handId, turnId = context.turnId,
    sequence = context.sequence, activeSeat = context.activeSeat, street = context.street,
    pot = gameTable:GetPotManager():GetAmount(), toCall = 0, minimumRaise = 0,
    communityCards = copyCards(gameTable:GetCommunityCards().cards), players = {}, revealedHands = {}
  }
  local private = {}
  for i, player in ipairs(gameTable:GetPlayers()) do
    local seat = assert(seats[i], "Unexpected table size")
    assert(player:GetName() == names[i], "Unexpected seat ordering")
    public.players[#public.players + 1] = {
      seat = seat, chips = player:GetChips(), contribution = player:GetChipsInPot(),
      folded = player:IsFolded(), allIn = player:IsAllIn(), eliminated = player:IsBusted()
    }
    private[seat] = copyCards(player:GetHand().holeCards)
    if gameTable.cardsTurnedOver and not player:IsFolded() then
      public.revealedHands[seat] = copyCards(player:GetHand().holeCards)
    end
    if context.activeSeat == seat then
      public.toCall = math.max(0, gameTable:GetCallValue() - player:GetChipsInPot())
      public.minimumRaise = public.toCall + math.max(gameTable:GetBigBlindValue(), LogicGet("iLastRaiseAmount") or 0)
    end
  end
  -- Host-only return value. Transport must project this into PublicTableState and one PrivateHand.
  return { public = public, holeCards = private }
end

function M.applyAction(gameTable, seat, action, state)
  local index = assert(indexBySeat[seat], "Unknown seat")
  local player = gameTable:GetPlayers()[index]
  assert(player and player:GetName() == names[index], "Unexpected player")
  assert(not player:IsFolded() and not player:IsBusted() and not player:IsAllIn(), "Inactive player")
  local toCall = math.max(0, gameTable:GetCallValue() - player:GetChipsInPot())
  local stack = player:GetChips()
  local kind = action.action
  local amount = action.amount or 0
  assert(type(amount) == "number" and amount == math.floor(amount) and amount >= 0 and amount <= stack, "Invalid amount")
  assert(kind == "raise" or amount == 0, "Unexpected amount")
  if kind == "fold" then
    -- GameTable:DoRoundState applies Fold and updates pots after this return value.
    return Player.Action.FOLD, "fold"
  elseif kind == "check" or kind == "call" then
    assert(kind ~= "check" or toCall == 0, "Cannot check")
    Player.Call(player)
    return Player.Action.CALL, player:GetChips() == 0 and "allIn" or toCall == 0 and "check" or "call"
  elseif kind == "raise" or kind == "allIn" then
    local contribution = kind == "allIn" and stack or amount
    if contribution <= toCall then
      assert(kind == "allIn", "Raise must exceed call")
      Player.Call(player)
      return Player.Action.CALL, "allIn"
    end
    local minimum = toCall + math.max(gameTable:GetBigBlindValue(), LogicGet("iLastRaiseAmount") or 0)
    assert(contribution >= minimum or contribution == stack, "Raise below minimum")
    -- Wire amount means chips paid now; the engine accepts the increment over its call value.
    Player.Raise(player, contribution - toCall, state)
    return Player.Action.RAISE, player:GetChips() == 0 and "allIn" or toCall == 0 and state.name ~= "ModePreFlop" and "bet" or "raise"
  end
  error("Unknown action")
end

function M.renderPrivateHand(gameTable, seat, hand)
  local localPlayer = gameTable:GetPlayers()[assert(indexBySeat[seat], "Unknown seat")]
  assert(#hand == 2, "A private hand needs two cards")
  -- Render only the local hand on the traditional camera's player card agents.
  for i, card in ipairs(hand) do
    assert(card.rank >= 2 and card.rank <= 14 and card.suit >= 1 and card.suit <= 4, "Invalid card")
    CardRenderer_Render("obj_cardPokerPlayer" .. i, Card(card.rank, card.suit))
  end
  LogicSet("sMP_LocalCharacter", localPlayer:GetName())
end

function M.describeSeats()
  local result = {}
  for i, name in ipairs(names) do result[i] = { seat = seats[i], gameName = name } end
  return result
end

return M
