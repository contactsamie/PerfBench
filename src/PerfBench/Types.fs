module PerfBench.Types

type Messages = Execute

type ExecutionResult = 
    | Success of string * string * float
    | Failure of string * string * float

type CoordinatorMessages = 
    | Create
    | Finished of ExecutionResult
    | Stats

type TaskStatus = 
    | Executing
    | Succeeded of string * float
    | Failed of string * float

type Events = 
    | StartedEvent of string
    | FinishedEvent of string * float
    | FailedEvent of string * float

type Swarm = 
    { Name : string
      Size : int }

type HiveEvents = 
    | CreateSwarm of Swarm
    | DroneReply of Events

type HiveBrains = 
    { Name : string
      Brain : unit -> Async<unit> }
