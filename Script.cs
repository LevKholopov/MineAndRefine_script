//Factor for logarithmic speed sacaling. Influenced by distance from the camera to the floor. 
//(if camera placed directly on horizontal drill the factor is about 4)
float CorrectionFactor = 4f;

//Piston speed limits. (For each piston)
float MaxPistonSpeed = 0.5f, MinPistonSpeed = -0.5f;

//This scrip has been observed to exceed 'maximum operations count per grid?'. (Things stoped transfering automaticly but the sim speed was fine. Weird...)
//This number determines for how many cycles script 'does nothing' to let the grid do it's own things. Increase as needed.
int idleCycles = 6;

//-----One doesn't simply changes someone else's code!-----//
IMyInventory _oreBuffer;
IMyInventory _produceStorage;
IMyInventory _iceStorage;
IMyConveyorSorter _mainDump;
IMyConveyorSorter _iceFilter;
IMyConveyorSorter _oreFilter;
IMyConveyorSorter _junkfilter;
IMyShipConnector _dock;
IMyDoor _airlockExterior;
IMyDoor _airlockInterior;
IMyTextPanel _textPanel;
IMyCameraBlock _distanceCam;
IMyGasTank _fuelTank;
IMyMotorAdvancedStator _drillRotor;
List<IMyShipDrill> _drills = new List<IMyShipDrill>();
List<IMyPistonBase> _pistons = new List<IMyPistonBase>();
List<IMyInventory> _refineInventories = new List<IMyInventory>();
Dictionary<string, object> runtimeData;

public Program()
{

    IMyBlockGroup blockGroup = GridTerminalSystem.GetBlockGroupWithName("M&R");
    List<IMyCubeBlock> list = new List<IMyCubeBlock>();
    blockGroup.GetBlocksOfType(list);

    Runtime.UpdateFrequency = UpdateFrequency.Update10;

    foreach (IMyTerminalBlock block in list)
    {
        if (block is IMyCargoContainer && block.CustomName.ToLower().Contains("ore")) _oreBuffer = ((IMyCargoContainer)block).GetInventory();
        else if (block is IMyCargoContainer && block.CustomName.ToLower().Contains("ice")) _iceStorage = ((IMyCargoContainer)block).GetInventory();
        else if (block is IMyCargoContainer && block.CustomName.ToLower().Contains("produce")) _produceStorage = ((IMyCargoContainer)block).GetInventory();

        else if (block is IMyConveyorSorter && block.CustomName.ToLower().Contains("icefilter")) _iceFilter = (IMyConveyorSorter)block;
        else if (block is IMyConveyorSorter && block.CustomName.ToLower().Contains("maindump")) _mainDump = (IMyConveyorSorter)block;
        else if (block is IMyConveyorSorter && block.CustomName.ToLower().Contains("junkfilter")) _junkfilter = (IMyConveyorSorter)block;
        else if (block is IMyConveyorSorter && block.CustomName.ToLower().Contains("orefilter")) _oreFilter = (IMyConveyorSorter)block;

        else if (block is IMyDoor && block.CustomName.ToLower().Contains("outer")) _airlockExterior = (IMyDoor)block;
        else if (block is IMyDoor && block.CustomName.ToLower().Contains("outer")) _airlockInterior = (IMyDoor)block;
        else if (block is IMyShipConnector && block.CustomName.ToLower().Contains("dock")) _dock = (IMyShipConnector)block;
        else if (block is IMyTextPanel) _textPanel = (IMyTextPanel)block;
        else if (block is IMyCameraBlock) { _distanceCam = (IMyCameraBlock)block; _distanceCam.EnableRaycast = true; }
        else if (block is IMyGasTank) _fuelTank = (IMyGasTank)block;

        else if (block is IMyMotorAdvancedStator) _drillRotor = (IMyMotorAdvancedStator)block;
        else if (block is IMyShipDrill) _drills.Add((IMyShipDrill)block);
        else if (block is IMyPistonBase) _pistons.Add((IMyPistonBase)block);
        else if (block is IMyRefinery) _refineInventories.Add(((IMyRefinery)block).OutputInventory);
    }

    runtimeData = new Dictionary<string, object>()
            {
                { "Current cycle", Cycle.Rest },
                { "Drilling speed", 0},
                { "Distance to bottom", 0 },
                { "Detected target", null },
                { "Fuel level", 0 },
                { "Ice storage", 0},
                { "Ore storage", 0 },
                { "Produce storage", 0 },
                { "Hauler docked", false },
                { "Drill mode", DrillMode.Idle },
                { "Ore found", false }
            };

    runtimeData.Add("Setup Status",
                $"Ore Buffer: {_oreBuffer != null}\n" +
                $"Produce Storage: {_produceStorage != null}\n" +
                $"Main dump sorter: {_mainDump != null}\n" +
                $"Junk filter: {_junkfilter != null}\n" +
                $"Ice filter: {_iceFilter != null}\n" +
                $"Ore blacklist filter: {_oreFilter != null}\n" +
                $"Text Panel: {_textPanel != null}\n" +
                $"Distance Cam: {_distanceCam != null}\n" +
                $"Fuel Tank: {_fuelTank != null}\n" +
                $"Dock connector: {_dock != null}\n" +
                $"Ailock doors: {_airlockExterior != null && _airlockInterior != null}\n" +
                $"Drill count: {_drills.Count}\n" +
                $"Piston count: {_pistons.Count}\n");
    Echo(runtimeData["Setup Status"].ToString());

    //Ore filter setup
    List<MyInventoryItemFilter> filterJunk = new List<MyInventoryItemFilter>();
    _junkfilter.GetFilterList(filterJunk);
    List<MyInventoryItemFilter> filterIce = new List<MyInventoryItemFilter>();
    _iceFilter.GetFilterList(filterIce);
    _oreFilter.SetFilter(MyConveyorSorterMode.Blacklist, filterJunk.Concat(filterIce).ToList());
}

//Main operations function
void Operate()
{
    Echo("Cycle:" + runtimeData["Current cycle"].ToString());
    Echo("Rest:" + (runtimeData["Current cycle"].ToString() == "Rest").ToString());
    Report();
    switch ((Cycle)runtimeData["Current cycle"])
    {
        case Cycle.Drill:
            {
                Mine(!(_oreBuffer.VolumeFillFactor > 0.9f) && !(_oreBuffer.VolumeFillFactor > 0.9f));
                break;
            }
        case Cycle.Report: { break; }
        case Cycle.DumpIce:
            {
                List<MyInventoryItemFilter> junkFilter = new List<MyInventoryItemFilter>();
                _junkfilter.GetFilterList(junkFilter);
                List<MyInventoryItemFilter> iceFilterList = new List<MyInventoryItemFilter>();
                _iceFilter.GetFilterList(iceFilterList);
                MyInventoryItemFilter ice = iceFilterList.Find(x => x.ItemType.SubtypeId.ToLower().Contains("ice"));
                if (_iceStorage.IsFull) junkFilter.Add(ice);
                else junkFilter.Remove(ice);
                _mainDump.SetFilter(MyConveyorSorterMode.Whitelist, junkFilter);
                break;
            }
        case Cycle.StoreProduce: //Move produce to storage
            {
                _refineInventories.ForEach(x => { int i = 0; while (i < x.ItemCount) x.TransferItemTo(_produceStorage, (MyInventoryItem)x.GetItemAt(i)); });
                break;
            }
        case Cycle.LoadProduce: //Load Hauler
            {
                if (_dock.IsConnected) LoadCargoShip();
                break;
            }
        case Cycle.OperateDoors:
            {
                if (_airlockInterior != null && _airlockExterior != null)
                {
                    _airlockExterior.Enabled = !(_airlockInterior.OpenRatio > 0);
                    _airlockInterior.Enabled = !(_airlockExterior.OpenRatio > 0);
                }
                break;
            }
        default: { break; };
    }
    if ((int)runtimeData["Current cycle"] > 7 + idleCycles)
        runtimeData["Current cycle"] = (Cycle)0;
    else runtimeData["Current cycle"] = (Cycle)runtimeData["Current cycle"] + 1;
}

//Loading cargo haulers
void LoadCargoShip()
{
    List<IMyCargoContainer> cargoShipContainers = new List<IMyCargoContainer>();
    GridTerminalSystem.GetBlocksOfType(cargoShipContainers, x => x is IMyCargoContainer);
    cargoShipContainers.RemoveAll(x => !x.CubeGrid.CustomName.ToLower().Contains("cargo") || x.CubeGrid != Me.CubeGrid || x.GetInventory().IsFull);
    if (cargoShipContainers.Count > 0 && _produceStorage.VolumeFillFactor > 0)
        cargoShipContainers[0].GetInventory().TransferItemFrom(_produceStorage, (MyInventoryItem)_produceStorage.GetItemAt(0));
}

//LCD report text
void Report()
{
    if (_textPanel != null)
    {
        runtimeData["Fuel level"] = _fuelTank.FilledRatio;
        runtimeData["Ore storage"] = _oreBuffer.VolumeFillFactor;
        runtimeData["Ice storage"] = _iceStorage.VolumeFillFactor;
        runtimeData["Produce storage"] = _produceStorage.VolumeFillFactor;
        runtimeData["Hauler docked"] = _dock.IsConnected;

        if (_textPanel.GetText().Length > 0) _textPanel.WriteText("");
        foreach (KeyValuePair<string, object> data in runtimeData)
            if (data.Key != "Setup Status")
            {
                if (data.Value is double || data.Value is float)
                    _textPanel.WriteText($"{data.Key}: {data.Value:f2}\n", true);
                else _textPanel.WriteText($"{data.Key}: {(data.Value ?? "NULL")}\n", true);
            }
    }
}

//Controlling drilling logic
void Mine(bool drilling)
{

    switch ((DrillMode)runtimeData["Drill mode"])
    {
        case DrillMode.Idle:
            {
                _pistons.ForEach(x => x.Velocity = -1);
                _drillRotor.Enabled = false;
                _drills.ForEach(x => x.Enabled = false);
                break;
            }

        case DrillMode.DrillingToOre:
            {
                _drills.ForEach(x => x.Enabled = drilling);
                _drillRotor.Enabled = drilling;
                _drills.ForEach(x => x.Enabled = true);
                Drill(true);
                if ((bool)runtimeData["Ore found"])
                    runtimeData["Drill mode"] = DrillMode.DrillingOre;
                break;
            }

        case DrillMode.DrillingOre:
            {
                _drills.ForEach(x => x.Enabled = drilling);
                _drillRotor.Enabled = drilling;
                Drill(true);
                if (!(bool)runtimeData["Ore found"])
                    runtimeData["Drill mode"] = DrillMode.Finished;
                break;
            }

        case DrillMode.Finished:
            {
                _pistons.ForEach((x) => x.Velocity = -1);
                _drillRotor.Enabled = false;
                _drills.ForEach(x => x.Enabled = false);
                break;
            }
    }
}

//Manages Drilling steps
void Drill(bool drillingOre)
{
    double distanceToGround = MeasureDistance();
    float velocity = CalcPistonVelocity(distanceToGround);
    _pistons.ForEach(x => x.Velocity = velocity);

    runtimeData["Drilling speed"] = velocity;
    runtimeData["Distance to bottom"] = distanceToGround;

    //Check if curently mining ore
    List<MyInventoryItem> items = new List<MyInventoryItem>();
    _drills.ForEach(x => x.GetInventory().GetItems(items));
    List<MyInventoryItemFilter> itemFilters = new List<MyInventoryItemFilter>();
    List<MyItemType> filteredItems = new List<MyItemType>();
    itemFilters.ForEach(x => filteredItems.Add(x.ItemType));

    bool miningOre;
    if (drillingOre) miningOre = items.Any(x => filteredItems.Any(y => y.SubtypeId == x.Type.SubtypeId));
    else miningOre = items.Any(x => !filteredItems.Any(y => y.SubtypeId == x.Type.SubtypeId));
    runtimeData["Ore found"] = miningOre;
}

//Calculating velocity of the pistons
float CalcPistonVelocity(double distanceToGround)
{
    float calcVelocity = (float)Math.Log10(distanceToGround - CorrectionFactor) / _pistons.Count;
    float velocity = 0;
    if (calcVelocity > MaxPistonSpeed) velocity = MaxPistonSpeed;
    else if (calcVelocity < MinPistonSpeed) velocity = MinPistonSpeed;
    else velocity = calcVelocity;
    return velocity;
}

//Taking distance measurment from camera. (Max distance for raycast calculated as _pistons.Count*10)
public double MeasureDistance()
{
    if (_distanceCam == null) { _textPanel?.WriteText($"Camera could not be found.", true); return 0; }
    _distanceCam.EnableRaycast = true;
    float maxDistance = _pistons.Count * 10;
    if (!_distanceCam.CanScan(maxDistance)) return 0;
    else
    {
        MyDetectedEntityInfo target = _distanceCam.Raycast(maxDistance);
        runtimeData["Detected target"] = target.Name;
        if (target.IsEmpty()) { Echo("Camera can't see the bottom!"); return -1; }
        Vector3D vector = new Vector3D(_distanceCam.GetPosition());
        return (float)(vector - (Vector3D)(target.HitPosition)).Length();
    }
}

enum Cycle
{
    Rest,
    Drill,
    DumpIce,
    StoreProduce,
    LoadProduce,
    OperateDoors,
    Report,
}

enum DrillMode
{
    Idle,
    DrillingToOre,
    DrillingOre,
    Finished
}

public void Save()
{

}

public void Main(string argument, UpdateType updateSource)
{
    switch (argument)
    {
        case "drill":
            { runtimeData["Drill mode"] = DrillMode.DrillingToOre; break; }
        case "stop":
            { runtimeData["Drill mode"] = DrillMode.Finished; break; }
        default: break;
    }
    Operate();
}