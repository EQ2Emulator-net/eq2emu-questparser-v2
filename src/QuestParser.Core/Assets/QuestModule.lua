QuestModule = QuestModule or {}

local unpackValues = unpack or table.unpack

local TARGET_STEP_TYPES = {
	basic = false,
	chat = true,
	kill = true,
	killSelfUpdate = true,
	killByRace = true,
	obtainItem = true,
	spell = true,
	craft = true,
	harvest = true
}

local LOCATION_STEP_TYPES = {
	location = true,
	zoneLoc = true
}

local PRESERVED_RANDOM_FIELDS = {
	"id",
	"type",
	"complete",
	"progress",
	"failed",
	"manualComplete"
}

local function raise(message)
	error("QuestModule: " .. message, 3)
end

local function isPositiveNumber(value)
	return type(value) == "number" and value > 0
end

local function isPositiveInteger(value)
	return isPositiveNumber(value) and value % 1 == 0
end

local function isNonEmptyString(value)
	return type(value) == "string" and value ~= ""
end

local function copyTable(source)
	local copy = {}
	for key, value in pairs(source) do
		copy[key] = value
	end
	return copy
end

local function getDescription(step)
	return step.description or step.desc or step.text or ""
end

local function getCountValue(step)
	if step.count ~= nil then
		return step.count
	end
	return step.quantity
end

local function getCount(step, defaultValue)
	local value = getCountValue(step)
	if value == nil then
		return defaultValue or 1
	end

	if type(value) == "table" then
		return MakeRandomInt(value.min, value.max)
	end

	return value
end

local function getPercentage(step)
	if step.percentage ~= nil then
		return step.percentage
	end
	if step.chance ~= nil then
		return step.chance
	end
	return 100
end

local function getTaskGroup(step)
	return step.groupDescription or step.taskGroupDescription or step.group or step.taskGroup or ""
end

local function getIcon(step)
	return step.icon or 0
end

local function getUsableItem(step)
	return step.usableItem or step.usableItemId or step.usableitemid or 0
end

local function getRadius(step)
	return step.radius or step.maxVariation or step.variation or 0
end

local function getTargets(step)
	return step.targets or step.targetIds or step.ids
end

local function addValues(target, values)
	if values == nil then
		return
	end

	for i = 1, #values do
		target[#target + 1] = values[i]
	end
end

local function hasCallback(step, field)
	return step[field] ~= nil and step[field] ~= false
end

local function validateCallback(step, field)
	if hasCallback(step, field) and not isNonEmptyString(step[field]) then
		raise("step " .. tostring(step.id) .. " has invalid " .. field .. " callback")
	end
end

local function validateCount(step)
	local value = getCountValue(step)
	if value == nil then
		return
	end

	if type(value) == "table" then
		if not isPositiveInteger(value.min) or not isPositiveInteger(value.max) then
			raise("step " .. tostring(step.id) .. " count range requires positive integer min and max")
		end
		if value.min > value.max then
			raise("step " .. tostring(step.id) .. " count range min exceeds max")
		end
	elseif not isPositiveInteger(value) then
		raise("step " .. tostring(step.id) .. " count must be a positive integer or a range table")
	end
end

local function validateTargets(step, required)
	local targets = getTargets(step)
	if targets == nil then
		if required then
			raise("step " .. tostring(step.id) .. " requires a targets array")
		end
		return
	end

	if type(targets) ~= "table" or #targets == 0 then
		raise("step " .. tostring(step.id) .. " targets must be a non-empty array")
	end

	for i = 1, #targets do
		if not isPositiveNumber(targets[i]) then
			raise("step " .. tostring(step.id) .. " target " .. tostring(i) .. " must be a positive number")
		end
	end
end

local function directLocationFromStep(step)
	if step.x ~= nil or step.y ~= nil or step.z ~= nil then
		return { step }
	end
	return nil
end

local function normalizeLocations(step)
	local locations = step.locations or step.location or directLocationFromStep(step)
	if locations == nil then
		return nil
	end

	if type(locations) ~= "table" then
		raise("step " .. tostring(step.id) .. " locations must be a table")
	end

	if locations.x ~= nil or locations.y ~= nil or locations.z ~= nil then
		return { locations }
	end

	if type(locations[1]) == "number" then
		local tupleSize = 3
		if step.type == "zoneLoc" then
			tupleSize = 4
		end

		if #locations == 0 or #locations % tupleSize ~= 0 then
			raise("step " .. tostring(step.id) .. " flat locations must contain complete " .. tostring(tupleSize) .. "-value tuples")
		end

		local normalized = {}
		for i = 1, #locations, tupleSize do
			local location = { locations[i], locations[i + 1], locations[i + 2] }
			if step.type == "zoneLoc" then
				location[4] = locations[i + 3]
			end
			normalized[#normalized + 1] = location
		end
		return normalized
	end

	return locations
end

local function getLocationCoordinate(location, key, index)
	if location[key] ~= nil then
		return location[key]
	end
	return location[index]
end

local function getLocationZone(location)
	if location.zone ~= nil then
		return location.zone
	end
	if location.zoneId ~= nil then
		return location.zoneId
	end
	if location.zoneID ~= nil then
		return location.zoneID
	end
	return location[4]
end

local function validateLocations(step)
	local locations = normalizeLocations(step)
	if type(locations) ~= "table" or #locations == 0 then
		raise("step " .. tostring(step.id) .. " requires a non-empty locations array")
	end

	for i = 1, #locations do
		local location = locations[i]
		if type(location) ~= "table" then
			raise("step " .. tostring(step.id) .. " location " .. tostring(i) .. " must be a table")
		end

		local x = getLocationCoordinate(location, "x", 1)
		local y = getLocationCoordinate(location, "y", 2)
		local z = getLocationCoordinate(location, "z", 3)
		if type(x) ~= "number" or type(y) ~= "number" or type(z) ~= "number" then
			raise("step " .. tostring(step.id) .. " location " .. tostring(i) .. " requires numeric x, y, and z")
		end

		if x == 0 and y == 0 and z == 0 then
			raise("step " .. tostring(step.id) .. " location " .. tostring(i) .. " cannot be all zero")
		end

		if step.type == "zoneLoc" and not isPositiveNumber(getLocationZone(location)) then
			raise("step " .. tostring(step.id) .. " zoneLoc location " .. tostring(i) .. " requires a positive numeric zone")
		end
	end
end

local function mergeRandomOption(step, option)
	local merged = copyTable(step)
	for key, value in pairs(option) do
		merged[key] = value
	end

	for i = 1, #PRESERVED_RANDOM_FIELDS do
		local key = PRESERVED_RANDOM_FIELDS[i]
		merged[key] = step[key]
	end

	merged.randomOptions = nil
	return merged
end

local function validateRandomOptions(step)
	if step.randomOptions == nil then
		return false
	end

	if type(step.randomOptions) ~= "table" or #step.randomOptions == 0 then
		raise("step " .. tostring(step.id) .. " randomOptions must be a non-empty array")
	end

	for i = 1, #step.randomOptions do
		if type(step.randomOptions[i]) ~= "table" then
			raise("step " .. tostring(step.id) .. " random option " .. tostring(i) .. " must be a table")
		end
		QuestModule.ValidateStep(mergeRandomOption(step, step.randomOptions[i]))
	end

	return true
end

local function resolveRandomStep(step)
	if step.randomOptions == nil then
		return step
	end

	local option = step.randomOptions[MakeRandomInt(1, #step.randomOptions)]
	return mergeRandomOption(step, option)
end

local function getLocationsForCall(step)
	local locations = normalizeLocations(step)
	local values = {}

	for i = 1, #locations do
		local location = locations[i]
		values[#values + 1] = getLocationCoordinate(location, "x", 1)
		values[#values + 1] = getLocationCoordinate(location, "y", 2)
		values[#values + 1] = getLocationCoordinate(location, "z", 3)
		if step.type == "zoneLoc" then
			values[#values + 1] = getLocationZone(location)
		end
	end

	return values
end

local function dispatchHandler(handler, Quest, QuestGiver, Player)
	if type(handler) == "function" then
		handler(Quest, QuestGiver, Player)
		return true
	end

	if isNonEmptyString(handler) and type(_G[handler]) == "function" then
		_G[handler](Quest, QuestGiver, Player)
		return true
	end

	return false
end

local function applyCompletionDescriptions(Quest, stage)
	local stepId = stage.step or stage.stepId or stage.id
	local stepDescription = stage.stepDescription or stage.completeDescription or stage.completedDescription or stage.stepCompleteDescription
	if stepId ~= nil and stepDescription ~= nil then
		UpdateQuestStepDescription(Quest, stepId, stepDescription)
	end

	local taskGroupId = stage.taskGroupId or stage.completeTaskGroupId or stage.completedTaskGroupId
	local taskGroupDescription = stage.completeTaskGroupDescription or stage.completedTaskGroupDescription or stage.taskGroupCompleteDescription
	if taskGroupId == nil and taskGroupDescription ~= nil then
		taskGroupId = stepId
	end

	if taskGroupId ~= nil and taskGroupDescription ~= nil then
		UpdateQuestTaskGroupDescription(Quest, taskGroupId, taskGroupDescription, stage.displayBullets)
	end
end

local function eachStep(steps)
	local index = 0
	return function()
		index = index + 1
		if steps[index] ~= nil then
			return steps[index]
		end
	end
end

function QuestModule.NoopLifecycle()
end

function QuestModule.ValidateStep(step)
	if type(step) ~= "table" then
		raise("step must be a table")
	end

	if not isPositiveInteger(step.id) then
		raise("step requires a positive integer id")
	end

	if not isNonEmptyString(step.type) then
		raise("step " .. tostring(step.id) .. " requires a type")
	end

	if TARGET_STEP_TYPES[step.type] == nil and LOCATION_STEP_TYPES[step.type] == nil then
		raise("step " .. tostring(step.id) .. " has unsupported type " .. tostring(step.type))
	end

	validateCallback(step, "complete")
	validateCallback(step, "progress")
	validateCallback(step, "failed")
	validateCount(step)

	if validateRandomOptions(step) then
		return true
	end

	if LOCATION_STEP_TYPES[step.type] then
		validateLocations(step)
	else
		validateTargets(step, TARGET_STEP_TYPES[step.type])
	end

	return true
end

function QuestModule.AddStep(Quest, step)
	QuestModule.ValidateStep(step)

	local activeStep = resolveRandomStep(step)
	QuestModule.ValidateStep(activeStep)

	local id = activeStep.id
	local stepType = activeStep.type
	local description = getDescription(activeStep)
	local count = getCount(activeStep, 1)
	local percentage = getPercentage(activeStep)
	local taskGroup = getTaskGroup(activeStep)
	local icon = getIcon(activeStep)
	local targets = getTargets(activeStep)

	if stepType == "basic" then
		local args = { Quest, id, description, count, percentage, taskGroup, icon, getUsableItem(activeStep) }
		addValues(args, targets)
		AddQuestStep(unpackValues(args))
	elseif stepType == "chat" then
		local args = { Quest, id, description, count, taskGroup, icon }
		addValues(args, targets)
		AddQuestStepChat(unpackValues(args))
	elseif stepType == "kill" then
		local args = { Quest, id, description, count, percentage, taskGroup, icon }
		addValues(args, targets)
		AddQuestStepKill(unpackValues(args))
	elseif stepType == "killSelfUpdate" then
		local args = { Quest, id, description, count, percentage, taskGroup, icon }
		addValues(args, targets)
		AddQuestStepKillSelfUpdate(unpackValues(args))
	elseif stepType == "killByRace" then
		local args = { Quest, id, description, count, percentage, taskGroup, icon }
		addValues(args, targets)
		AddQuestStepKillByRace(unpackValues(args))
	elseif stepType == "obtainItem" then
		local args = { Quest, id, description, count, percentage, taskGroup, icon }
		addValues(args, targets)
		AddQuestStepObtainItem(unpackValues(args))
	elseif stepType == "zoneLoc" then
		local args = { Quest, id, description, getRadius(activeStep), taskGroup, icon }
		addValues(args, getLocationsForCall(activeStep))
		AddQuestStepZoneLoc(unpackValues(args))
	elseif stepType == "location" then
		local args = { Quest, id, description, getRadius(activeStep), taskGroup, icon }
		addValues(args, getLocationsForCall(activeStep))
		AddQuestStepLocation(unpackValues(args))
	elseif stepType == "spell" then
		local args = { Quest, id, description, count, percentage, taskGroup, icon }
		addValues(args, targets)
		AddQuestStepSpell(unpackValues(args))
	elseif stepType == "craft" then
		local args = { Quest, id, description, count, percentage, taskGroup, icon }
		addValues(args, targets)
		AddQuestStepCraft(unpackValues(args))
	elseif stepType == "harvest" then
		local args = { Quest, id, description, count, percentage, taskGroup, icon }
		addValues(args, targets)
		AddQuestStepHarvest(unpackValues(args))
	end

	if isNonEmptyString(activeStep.complete) and not activeStep.manualComplete then
		AddQuestStepCompleteAction(Quest, id, activeStep.complete)
	end

	if isNonEmptyString(activeStep.progress) then
		AddQuestStepProgressAction(Quest, id, activeStep.progress)
	end

	if isNonEmptyString(activeStep.failed) then
		AddQuestStepFailureAction(Quest, id, activeStep.failed)
	end

	return activeStep
end

function QuestModule.AddSteps(Quest, steps)
	for step in eachStep(steps) do
		QuestModule.AddStep(Quest, step)
	end
end

function QuestModule.CompleteStage(Quest, QuestGiver, Player, stage, steps)
	if type(stage) == "number" and steps ~= nil then
		stage = steps[stage]
	end

	if type(stage) ~= "table" then
		raise("CompleteStage requires a stage table or valid stage id")
	end

	applyCompletionDescriptions(Quest, stage)

	if stage.nextSteps ~= nil then
		QuestModule.AddSteps(Quest, stage.nextSteps)
	elseif stage.next ~= nil then
		QuestModule.AddSteps(Quest, stage.next)
	end

	if type(stage.onComplete) == "function" then
		stage.onComplete(Quest, QuestGiver, Player, stage, steps)
	end

	if stage.questComplete then
		local completion = stage.questComplete
		if completion == true then
			completion = stage
		end
		QuestModule.CompleteQuest(Quest, Player, completion)
	end

	return true
end

function QuestModule.ExportStepHandlers(steps, options)
	options = options or {}
	local handlers = {}
	local callbacks = {}

	for step in eachStep(steps) do
		QuestModule.ValidateStep(step)
		if isNonEmptyString(step.complete) and not step.manualComplete then
			if callbacks[step.complete] then
				raise("duplicate completion callback " .. step.complete)
			end
			callbacks[step.complete] = true

			if _G[step.complete] ~= nil and not options.overwrite then
				raise("refusing to overwrite existing global callback " .. step.complete)
			end

			local capturedStep = step
			local handler = function(Quest, QuestGiver, Player)
				return QuestModule.CompleteStage(Quest, QuestGiver, Player, capturedStep, steps)
			end

			_G[step.complete] = handler
			handlers[step.id] = handler
		end
	end

	return handlers
end

function QuestModule.ExportStageStepHandlers(stages, options)
	local allSteps = {}

	for steps in eachStep(stages or {}) do
		for step in eachStep(steps) do
			allSteps[#allSteps + 1] = step
		end
	end

	QuestModule.ExportStepHandlers(allSteps, options)
	return allSteps
end

function QuestModule.AllComplete(Player, questId, steps)
	for step in eachStep(steps) do
		local stepId = step
		if type(step) == "table" then
			stepId = step.id
		end

		if not QuestStepIsComplete(Player, questId, stepId) then
			return false
		end
	end

	return true
end

function QuestModule.OnAllComplete(Quest, QuestGiver, Player, questId, steps, action)
	if not QuestModule.AllComplete(Player, questId, steps) then
		return false
	end

	if type(action) == "function" then
		action(Quest, QuestGiver, Player)
	elseif isNonEmptyString(action) then
		if type(_G[action]) ~= "function" then
			raise("all-complete callback " .. action .. " is not defined")
		end
		_G[action](Quest, QuestGiver, Player)
	elseif type(action) == "table" then
		QuestModule.CompleteQuest(Quest, Player, action)
	end

	return true
end

function QuestModule.ReloadByStep(Quest, QuestGiver, Player, Step, handlers, steps)
	if handlers ~= nil and handlers[Step] ~= nil then
		return dispatchHandler(handlers[Step], Quest, QuestGiver, Player)
	end

	if steps ~= nil then
		for step in eachStep(steps) do
			if step.id == Step then
				return dispatchHandler(step.complete, Quest, QuestGiver, Player)
			end
		end
		return false
	end

	return dispatchHandler("Step" .. tostring(Step) .. "Complete", Quest, QuestGiver, Player)
end

function QuestModule.CompleteQuest(Quest, PlayerOrQuestGiver, maybePlayer, maybeCompletion)
	local Player = nil
	local completion = nil

	if maybeCompletion ~= nil then
		Player = maybePlayer
		completion = maybeCompletion
	else
		Player = PlayerOrQuestGiver
		completion = maybePlayer
	end

	if type(completion) ~= "table" then
		raise("CompleteQuest requires completion data table")
	end

	local stepId = completion.step or completion.stepId or completion.id
	local stepDescription = completion.stepDescription or completion.completeDescription or completion.completedDescription or completion.stepCompleteDescription
	if stepId ~= nil and stepDescription ~= nil then
		UpdateQuestStepDescription(Quest, stepId, stepDescription)
	end

	local taskGroupId = completion.taskGroupId or completion.completeTaskGroupId or completion.completedTaskGroupId
	if taskGroupId == nil and type(completion.taskGroup) == "number" then
		taskGroupId = completion.taskGroup
	end
	if taskGroupId == nil and type(completion.group) == "number" then
		taskGroupId = completion.group
	end

	local taskGroupDescription = completion.taskGroupDescription or completion.completeTaskGroupDescription or completion.completedTaskGroupDescription or completion.taskGroupCompleteDescription
	if taskGroupId ~= nil and taskGroupDescription ~= nil then
		UpdateQuestTaskGroupDescription(Quest, taskGroupId, taskGroupDescription, completion.displayBullets)
	end

	local questDescription = completion.description or completion.questDescription or completion.completeQuestDescription
	if questDescription ~= nil then
		UpdateQuestDescription(Quest, questDescription)
	end

	GiveQuestReward(Quest, Player)
end

function QuestModule.ApplyMetadata(Quest, metadata)
	if metadata == nil then
		return
	end

	if type(metadata) ~= "table" then
		raise("metadata must be a table")
	end

	local featherColor = metadata.featherColor or metadata.feather
	if featherColor ~= nil then
		SetQuestFeatherColor(Quest, featherColor)
	end

	if metadata.repeatable then
		SetQuestRepeatable(Quest)
	end

	local zone = metadata.zone or metadata.questZone
	if zone ~= nil then
		UpdateQuestZone(Quest, zone)
	end
end

function QuestModule.BuildNamedSteps(steps)
	if type(steps) ~= "table" then
		raise("BuildNamedSteps requires a table")
	end

	local built = {}
	local maxId = 0
	local callbacks = {}

	for key, step in pairs(steps) do
		if type(step) ~= "table" then
			raise("step " .. tostring(key) .. " must be a table")
		end

		local builtStep = copyTable(step)
		if builtStep.id == nil and type(key) == "number" then
			builtStep.id = key
		end
		if builtStep.name == nil and type(key) == "string" then
			builtStep.name = key
		end

		QuestModule.ValidateStep(builtStep)

		if built[builtStep.id] ~= nil then
			raise("duplicate step id " .. tostring(builtStep.id))
		end

		for _, field in ipairs({ "complete", "progress", "failed" }) do
			local callback = builtStep[field]
			if isNonEmptyString(callback) then
				if callbacks[callback] ~= nil then
					raise("duplicate callback " .. callback)
				end
				callbacks[callback] = true
			end
		end

		built[builtStep.id] = builtStep
		if builtStep.id > maxId then
			maxId = builtStep.id
		end
	end

	for id = 1, maxId do
		if built[id] == nil then
			raise("missing contiguous step id " .. tostring(id))
		end
	end

	return built
end
