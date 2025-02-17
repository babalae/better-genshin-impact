namespace BetterGenshinImpact.Hutao;

internal enum PipeRequestKind
{
    None = 0,                            // ContractVersion 1, Both
    GetContractVersion = 1,              // ContractVersion 1, Both
    StartCapture = 2,                    // ContractVersion 1, S to B
    StopCapture = 3,                     // ContractVersion 1, S to B
    Log = 4,                             // ContractVersion 1, B to S
    QueryTaskArray = 10,                 // ContractVersion 1, S to B
    StartTask = 11,                      // ContractVersion 1, S to B
    CreateOneShotTask = 20,              // ContractVersion 1, B to S
    CreateSteppedTask = 21,              // ContractVersion 1, B to S
    RemoveTask = 22,                     // ContractVersion 1, B to S
    UpdateTaskDefinition = 23,           // ContractVersion 1, B to S
    UpdateTaskStepDefinition = 24,       // ContractVersion 1, B to S
    UpdateTaskStepIndex = 25,            // ContractVersion 1, B to S
    AddTaskStepDefinition = 26,          // ContractVersion 1, B to S
    BeginSwitchToNextGameAccount = 30,   // ContractVersion 1, B to S
    EndSwitchToNextGameAccount = 31,     // ContractVersion 1, S to B
    QueryCurrentCultivationProject = 40, // ContractVersion 1, B to S
}