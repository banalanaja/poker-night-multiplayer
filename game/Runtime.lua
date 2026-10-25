local installation = dofile("PNM_InstallConfig.lua")
local config = installation
local json = dofile("PNM_Json.lua")
local adapter = dofile("PNM_Adapter.lua")
local runtime = { loaded=true, turnId=0, sequence=0, lastCommand=0, localSequence=0, activeSeat=nil, street="idle", started=false }
PNM_Runtime = runtime
local names = { Max="max", Strongbad="strongBad", Heavy="heavy", Tycho="tycho", Player="player" }
local seatNames={max="Max",strongBad="Strongbad",heavy="Heavy",tycho="Tycho",player="Player"}
local seatIndices={max=1,strongBad=2,heavy=3,tycho=4,player=5}
local view,inputProxy,currentHand,uiTurn,lastViewSequence,lastVisualTurn
local original = {}
local function path(name) return config.directory .. "/" .. name end
local function write(name,value)
  local temporary=path(name .. ".tmp")
  local file=io.open(temporary,"wb");if not file then return false end
  file:write(json.encode(value));file:close()
  os.remove(path(name));local ok=os.rename(temporary,path(name));return ok~=nil
end
local function read(name)
  local file=io.open(path(name),"rb");if not file then return nil end
  local text=file:read(65537);file:close()
  if not text or #text>65536 then return nil end
  local ok,result=pcall(json.decode,text);if ok then return result end
end
config=read("session-config.json")
if not config then return end
config.directory=installation.directory
local previousEnd=read("end.json")
if previousEnd and previousEnd.sessionId==config.sessionId then return end
assert(config.role=="host" or config.role=="client","Invalid role")
local function protectProgress()
  local values,present={},{}
  local getter=PersistentData_Get
  PersistentData_Get=function(key) if present[key] then return values[key] end;return getter(key) end
  PersistentData_Set=function(key,value) present[key]=true;values[key]=value end
  PersistentData_Remove=function(key) present[key]=true;values[key]=nil end
  for _,name in ipairs({"SavePrefs","SaveGame","PersistentData_Save","CelebrityPoker_SaveGame","AchievementManager_Unlock"}) do
    if type(_G[name])=="function" then original[name]=_G[name];_G[name]=function() end end
  end
  -- Original debug output includes all players' cards. Suppress it in this isolated session.
  original.print=print;original.Print=Print;print=function() end;Print=function() end
  PersistentData_Set("Showed Tutorial",true)
end
local function markArrays(frame)
  json.array(frame.public.communityCards);json.array(frame.public.players)
  for _,cards in pairs(frame.holeCards) do json.array(cards) end
  for _,cards in pairs(frame.public.revealedHands) do json.array(cards) end
  if not frame.public.activeSeat then frame.public.activeSeat=json.null end
end
local function snapshot()
  if not (GameObject and GameObject.GetTable) then return end
  local gameTable=GameObject:GetTable()
  if not gameTable or not gameTable.players or #gameTable.players~=5 then return end
  runtime.sequence=runtime.sequence+1
  local frame=adapter.capture(gameTable,{sessionId=config.sessionId,handId=gameTable.handNumber or 0,
    turnId=runtime.turnId,sequence=runtime.sequence,activeSeat=runtime.activeSeat,street=runtime.street})
  frame.public.lastAction=runtime.lastAction
  markArrays(frame)
  write("host-frame.json",frame)
  return frame
end
local function disableInput()
  if uiTurn then ChorePlay("ui_player_deactivate.chore") end
  LogicSet("Enable Betting UI",false);uiTurn=nil
end
local function queueLocalAction(self,code)
  if not view or runtime.pendingLocalTurn==view.public.turnId or view.public.activeSeat~=config.localSeat then return end
  local kind=({[Player.Action.CALL]="call",[Player.Action.FOLD]="fold",[Player.Action.RAISE]="raise"})[code]
  if not kind then return end
  runtime.localSequence=runtime.localSequence+1
  local amount=kind=="raise" and self.betValue or 0
  if kind=="raise" and amount>=self:GetChips() then kind="allIn";amount=0 end
  write("local-action.json",{sessionId=config.sessionId,handId=view.public.handId,turnId=view.public.turnId,
    sequence=runtime.localSequence,seat=config.localSeat,action=kind,amount=amount})
  runtime.pendingLocalTurn=view.public.turnId;disableInput()
end
local function updateInput(gameTable)
  if not runtime.tableReady or not view or not inputProxy or not SceneIsActive("ui_player.scene") then return end
  local own
  for _,player in ipairs(view.public.players) do if player.seat==config.localSeat then own=player end end
  if not own then return end
  AgentHide("ui_player_buttonFoldSkipParent",true)
  AgentSetSelectable("ui_player_buttonFoldSkip",false)
  inputProxy.chips=own.chips;inputProxy.chipsInPot=own.contribution
  inputProxy.folded=own.folded;inputProxy.allIn=own.allIn
  if config.role=="host" then inputProxy.cHand=gameTable.players[seatIndices[config.localSeat]].cHand end
  HumanPlayerUI_SetPlayer(inputProxy)
  if #view.private.cards==2 then adapter.renderPrivateHand(gameTable,config.localSeat,view.private.cards) end
  for seat,name in pairs(seatNames) do
    local label=seat=="max" and "txt_max" or "txt_" .. name
    AgentSetProperty(label,kText,(view.names[seat] or "Bot") .. " / " .. name .. (seat==config.localSeat and " [VOCÊ]" or ""))
  end
  local active=view.public.activeSeat
  if active~=json.null then LogicSet("sActivePlayer",seatNames[active] or "") end
  local allowed=active==config.localSeat and not own.folded and not own.allIn and not own.eliminated
    and runtime.pendingLocalTurn~=view.public.turnId
  if allowed and uiTurn~=view.public.turnId then
    uiTurn=view.public.turnId
    if config.role=="client" then gameTable.callValue=own.contribution+view.public.toCall end
    LogicSet("iLastRaiseAmount",math.max(0,view.public.minimumRaise-view.public.toCall))
    HumanPlayerUI_ResetSlider();inputProxy:SetBetValueFromSlider(0)
    AgentSetProperty("btn_call",kTextNode,view.public.toCall==0 and "btn_check" or "btn_call")
    ChorePlay("ui_player_activate.chore");LogicSet("Enable Betting UI",true)
  elseif not allowed and uiTurn then disableInput() end
end
local function originalSeatBot(player,state)
  local gameTable=player.parentTable
  player.pnmAI=player.pnmAI or PokerAI(10,5)
  local board=BoardState()
  board.numChips=player:GetChips();board.bigBlind=gameTable:GetBigBlindValue()
  board.callValue=math.max(0,gameTable:GetCallValue()-player:GetChipsInPot())
  board.potSize=gameTable:GetPotManager():GetAmount();board.minRaise=board.callValue+LogicGet("iLastRaiseAmount")
  board.mediumRaise=board.minRaise+board.bigBlind;board.bigRaise=board.minRaise+4*board.bigBlind
  board.checkFold=board.callValue==0 and 0 or -1;board.position=player:GetPosition(state)
  board.communityCards=Hand();board.communityCards:AddCommunityCards(gameTable:GetCommunityCards().cards)
  board.numPlayers=#gameTable:GetActivePlayers();board.hand=player:GetHand()
  local result
  if state.name=="ModePreFlop" then result=player.pnmAI:BetPreflop(board) else result=player.pnmAI:BetPostflop(board) end
  result=math.floor(result/100)*100
  local command={action=result<0 and "fold" or result<=board.callValue and "call" or "raise",amount=0}
  if command.action=="raise" then command.amount=math.min(board.numChips,math.max(result,board.minRaise)) end
  return adapter.applyAction(gameTable,"player",command,state)
end
local function replica(gameTable)
  if not runtime.tableReady or not view or view.public.sequence==lastViewSequence then return end
  lastViewSequence=view.public.sequence
  local newHand=currentHand~=view.public.handId;currentHand=view.public.handId;gameTable.handNumber=currentHand
  if newHand then
    gameTable:ClearCommunityCards()
    for _,player in ipairs(gameTable.players) do
      player:HideHandUI(true,true);player:HideHoleCardUI(true,true);player.cHand=Hand()
    end
  end
  for _,item in ipairs(view.public.players) do
    local player=gameTable.players[seatIndices[item.seat]]
    player.chips=item.chips;player.chipsInPot=item.contribution;player.allIn=item.allIn;player.folded=item.folded
    LogicSet("iChipcount_" .. player:GetName(),item.chips)
    AgentSetProperty("txt_chipcount_" .. player:GetName(),kText,UI_ChipsToString(item.chips))
    if newHand and not item.eliminated then
      for i=1,2 do ChorePlay(player.dealChores[i]);Wait(0.17) end
    end
  end
  local community=CardContainer()
  for _,card in ipairs(view.public.communityCards) do community:Add(Card(card.rank,card.suit)) end
  gameTable.cCommunityCards=Hand();gameTable.cCommunityCards:AddCommunityCards(community)
  local own=gameTable.players[seatIndices[config.localSeat]];own.cHand=Hand()
  for _,card in ipairs(view.private.cards) do own.cHand:AddHoleCard(Card(card.rank,card.suit)) end
  own.cHand:AddCommunityCards(community);inputProxy.cHand=own.cHand
  if #view.private.cards==2 then own.cHand:Evaluate() end
  for seat,cards in pairs(view.public.revealedHands) do
    local player=gameTable.players[seatIndices[seat]];player.cHand=Hand()
    for _,card in ipairs(cards) do player.cHand:AddHoleCard(Card(card.rank,card.suit)) end
    player.cHand:AddCommunityCards(community)
    if #cards==2 then
      if #view.public.communityCards==5 then
        player.cHand:Evaluate();if not player.showingHandCards then player:ShowHandUI() end
      elseif not player.showingHoleCards then player:ShowHoleCardUI() end
    end
  end
  gameTable:RenderCards();LogicSet("iPotValue",view.public.pot)
  if newHand then ChorePlay("ui_player_potActivate.chore") end
  for i=1,#view.public.communityCards do AgentHide("obj_cardComm" .. i,false) end
  if #view.public.communityCards>=3 then for i=1,3 do AgentHide("obj_cardPokerFlop" .. i,false) end end
  if #view.public.communityCards>=4 then AgentHide("obj_cardPokerTurn",false) end
  if #view.public.communityCards>=5 then AgentHide("obj_cardPokerRiver",false) end
  AgentSetProperty("txt_potAmount",kText,UI_ChipsToString(view.public.pot))
  CameraManager_SetActiveCamera()
  local event=view.public.lastAction
  if event and event~=json.null and event.turnId~=lastVisualTurn then
    lastVisualTurn=event.turnId
    local actor=gameTable.players[seatIndices[event.seat]];actor.lastBetAmount=event.amount
    local code=event.action=="fold" and Player.Action.FOLD or event.action=="raise" and Player.Action.RAISE or Player.Action.CALL
    actor:FireActionChore(code,event.action)
  end
end
local function startIfRequested()
  if runtime.inGame then return end
  local request=read("start.json")
  if not request or request.sessionId~=config.sessionId then return end
  local latest=read("session-config.json")
  if latest and latest.sessionId==config.sessionId then config.controlledSeats=latest.controlledSeats end
  runtime.started=true
  -- The native Play Game callback closes its initialized menu before entering the table.
  -- Starting here races menu initialization and can leave the menu over the poker scene.
end
local function awaitAction(player,state)
  Player.EvaluateAndAct(player,state)
  local gameTable=player.parentTable
  snapshot()
  while true do
    local latest=read("session-config.json")
    if latest and latest.sessionId==config.sessionId then config.controlledSeats=latest.controlledSeats end
    if not config.controlledSeats[runtime.activeSeat] then
      if runtime.activeSeat=="player" then return originalSeatBot(player,state) end
      return original.aiEvaluate(player,state)
    end
    local action=read("command.json")
    if action and action.sessionId==config.sessionId and action.handId==gameTable.handNumber and action.turnId==runtime.turnId
      and action.seat==runtime.activeSeat and type(action.sequence)=="number" and action.sequence==math.floor(action.sequence)
      and action.sequence>runtime.lastCommand then
      local result={pcall(adapter.applyAction,gameTable,runtime.activeSeat,action,state)}
      runtime.lastCommand=action.sequence;os.remove(path("command.json"))
      if result[1] then
        write("action-result.json",{accepted=true,sessionId=config.sessionId,handId=gameTable.handNumber,
          turnId=runtime.turnId,sequence=action.sequence,seat=runtime.activeSeat,action=action.action})
        return result[2],result[3]
      else
        write("action-result.json",{accepted=false,sequence=action.sequence,reason="invalid_action"})
      end
    end
    WaitForNextFrame()
  end
end
local function createInputProxy(gameTable)
  local proxy=HumanPlayer("Player",gameTable);proxy.QueueAction=queueLocalAction
  -- Solo fast-forward must never make decisions for remote humans.
  proxy.OnButtonA=function(self,event)
    if LogicGet("Enable Betting UI") then return self:QueueAction(Player.Action.CALL) end
    if config.role=="host" and LogicGet("bWaitingToDeal") then GameObject:OnButtonDeal() end
  end
  proxy.OnMouseLUp=function(self,button)
    if button and AgentGetName(button)=="ui_player_buttonFoldSkip" then return end
    return HumanPlayer.OnMouseLUp(self,button)
  end
  return proxy
end
local function install()
  protectProgress()
  original.aiEvaluate=AIPlayer.EvaluateAndAct
  local launch=CelebrityPoker
  CelebrityPoker=function(...)
    if runtime.inGame then return end
    runtime.nativeStartPending=true
    while not runtime.started do WaitForNextFrame() end
    runtime.inGame=true
    LogicSet("bSilentBuyIn",true);GameObject.skipOpeningSequence=true
    -- Multiplayer always starts a separate tournament, regardless of a solo save.
    local result={launch(false)};runtime.tableReady=true
    return table.unpack(result)
  end
  local getPlayer=GameTable.GetPlayer
  GameTable.GetPlayer=function(self,index) if index==nil and inputProxy and inputProxy.parentTable==self then return inputProxy end;return getPlayer(self,index) end
  local initialize=GameTable.Initialize
  GameTable.Initialize=function(self,...)
    initialize(self,...);inputProxy=createInputProxy(self)
    lastViewSequence=nil;currentHand=nil;lastVisualTurn=nil
  end
  inputProxy=createInputProxy(GameObject:GetTable())
  if config.role=="host" then
    local endTournament=EndTournament
    EndTournament=function(kind,...)
      if kind==3 and #GameObject:GetTable():GetNonBustedPlayers()>1 then return end
      return endTournament(kind,...)
    end
    local resolvePot=GameTable.ResolvePot
    GameTable.ResolvePot=function(self,...)
      runtime.activeSeat=nil;runtime.street="showdown"
      return resolvePot(self,...)
    end
    local doRound=GameTable.DoRound
    GameTable.DoRound=function(self,...)
      runtime.activeSeat=nil;runtime.street="dealing"
      local result={doRound(self,...)}
      runtime.activeSeat=nil;runtime.street="complete";snapshot()
      return table.unpack(result)
    end
    for _,class in ipairs({HumanPlayer,AIPlayer}) do
    local previous=class.EvaluateAndAct
    class.EvaluateAndAct=function(self,state)
      runtime.turnId=runtime.turnId+1
      runtime.activeSeat=assert(names[self:GetName()],"Unexpected player")
      runtime.street=({ModePreFlop="preflop",ModeFlop="flop",ModeTurn="turn",ModeRiver="river"})[state.name] or state.name
      snapshot()
      local before=self:GetChips();local a,b
      if config.controlledSeats[runtime.activeSeat] then a,b=awaitAction(self,state)
      elseif runtime.activeSeat=="player" then a,b=originalSeatBot(self,state)
      else a,b=previous(self,state) end
      runtime.lastAction={seat=runtime.activeSeat,action=b=="bet" and "raise" or b,amount=before-self:GetChips(),turnId=runtime.turnId}
      runtime.activeSeat=nil
      return a,b
    end
  end else
    Game.RunTournament=function() LogicSet("bInGame",true);CameraManager_SetActiveCamera();UI_HideCursor(false) end
    Game.DoRound=function() end
    Game.OnButtonDeal=function() end
  end
  GameObject.skipOpeningSequence=true
  runtime.installed=true
  write("status.json",{loaded=true,installed=true,lua=_VERSION,sessionId=config.sessionId,role=config.role,progressIsolated=true})
  local elapsed=0
  while true do
    local ended=read("end.json")
    if ended and ended.sessionId==config.sessionId then disableInput();write("status.json",{loaded=true,installed=true,ended=true,sessionId=config.sessionId});return end
    startIfRequested()
    local incoming=read("view.json")
    if incoming and incoming.public.sessionId==config.sessionId and incoming.private.seat==config.localSeat then view=incoming end
    local error=read("action-error.json")
    if error and view and error.sessionId==config.sessionId and error.turnId==view.public.turnId then runtime.pendingLocalTurn=nil;os.remove(path("action-error.json")) end
    local stage="bindings"
    local ok=pcall(function()
      if not runtime.agentBindings and SceneIsActive("ui_player") then
        write("ui-agents.json",{agents=json.array(SceneGetAgentNames("ui_player.scene"))});runtime.agentBindings=true
      end
      stage="table"
      if config.role=="host" then
        local latest=read("session-config.json");if latest and latest.sessionId==config.sessionId then config.controlledSeats=latest.controlledSeats end
        snapshot()
      else replica(GameObject:GetTable()) end
      stage="input";updateInput(GameObject:GetTable())
    end)
    if not ok then write("status.json",{loaded=true,installed=true,snapshotError=true,stage=stage,sessionId=config.sessionId,role=config.role}) end
    repeat WaitForNextFrame();elapsed=elapsed+GetAverageFrameTime() until elapsed>=0.1
    elapsed=0
  end
end
write("status.json",{loaded=true,lua=_VERSION,sessionId=config.sessionId})
ThreadStart(function()
  while not (GameObject and HumanPlayer and AIPlayer and Player and GameObject:GetTable()) do WaitForNextFrame() end
  local ok=pcall(install)
  if not ok then write("status.json",{loaded=true,installed=false,error="runtime_install_failed"}) end
end)
