module PerfBench.Types

type Messages = Execute

type ExecutionResult = 
  | Success
  | Failure of string

type CoordinatorMessages = 
  | Create
  | Finished of ExecutionResult * string * float
  | Stats

type TaskStatus = 
  | Executing
  | Succeeded of float
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
    brain : unit -> Async<unit> }