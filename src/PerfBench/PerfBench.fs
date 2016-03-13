module PerfBench.Engine

open Akka.FSharp
open Akka.Configuration
open Akka.Actor
open Akka.Configuration
open Akka.FSharp
open System
open System.Text
open System.Reflection
open System.Resources
open System.IO
open System.Linq
open HttpClient
open FSharp.Data
open FSharp.Data.JsonExtensions
open Newtonsoft.Json
open Nessos.FsPickler
open Nessos.FsPickler.Json
open Suave
open Suave.Http
open Suave.Operators
open Suave.Filters
open Suave.Successful
open Suave.Files
open Suave.RequestErrors
open Suave.Logging
open Suave.Utils
open Suave.Sockets
open Suave.Sockets.Control
open Suave.WebSocket
open System.Collections.Generic
open System.Threading

// initialize akka
let private config = @"
    akka {
        log-config-on-start = on
        stdout-loglevel = DEBUG
        loglevel = DEBUG
        # this config section will be referenced as akka.actor
        actor { 
          debug {
              receive = on
              autoreceive = on
              lifecycle = on
              event-stream = on
              unhandled = on
          }
        }
    }"
let private config2 = @""
let private system = System.create "MySystem" <| Configuration.parse (config2)

// Load testing functionality
type private Messages = Execute

type private ExecutionResult = 
  | Success
  | Failure of string

type private CoordinatorMessages = 
  | Create
  | Finished of ExecutionResult * string * float
  | Stats

type private TaskStatus = 
  | Executing
  | Succeeded of float
  | Failed of string * float

type private Events = 
  | StartedEvent of string
  | FinishedEvent of string * float
  | FailedEvent of string * float

type private Swarm = 
  { Name : string
    Size : int }

type private HiveEvents = 
  | CreateSwarm of Swarm
  | DroneReply of Events

type HiveBrains = 
  { Name : string
    brain : unit -> Async<unit> }

let private drone name brain (parent : Actor<CoordinatorMessages>) = 
  spawn parent name <| fun droneMailbox -> 
    //printfn "starting drone %s" name
    let timer = new System.Diagnostics.Stopwatch()
    do droneMailbox.Defer(fun () -> 
         printfn "failed %s" name
         parent.Self <! Finished(Failure "Unknown Error", name, timer.Elapsed.TotalSeconds))
    let rec loop state = 
      actor { 
        let! msg = droneMailbox.Receive()
        //printf "%A" msg
        match msg with
        | Execute -> 
          async { 
            timer.Start()
            let! result = brain() |> Async.Catch
            timer.Stop()
            match result with
            | Choice1Of2 _ -> return Finished(Success, name, timer.Elapsed.TotalSeconds)
            | Choice2Of2 exn -> return Finished(Failure exn.Message, name, timer.Elapsed.TotalSeconds)
          }
          |!> parent.Self
      }
    loop []

let private getAverage list = 
  match list with
  | [] -> 0.0
  | l -> 
    l |> List.averageBy (fun e -> 
           match e with
           | n, (r, Succeeded t) -> t
           | n, (r, Failed(s, t)) -> t)

let private getMax list = 
  match list with
  | [] -> 0.0
  | l -> 
    let maxElement = 
      l |> List.maxBy (fun e -> 
             match e with
             | n, (r, Succeeded t) -> t
             | n, (r, Failed(s, t)) -> t)
    match maxElement with
    | n, (r, Succeeded t) -> t
    | n, (r, Failed(s, t)) -> t

let private printStats state time = 
  let (successfulDrones, failedDrones) = 
    state |> List.partition (fun e -> 
               match e with
               | n, (r, Succeeded s) -> true
               | _ -> false)
  
  let successfulAverage = getAverage successfulDrones
  let failedAverage = getAverage failedDrones
  let successfulMax = getMax successfulDrones
  let failedMax = getMax failedDrones
  printfn ""
  printfn "Total: %d drones" state.Length
  printfn "Processing Time: %f " time
  printfn ""
  printfn "Succeeded: %d drones" successfulDrones.Length
  printfn "Average processing time is %f" successfulAverage
  printfn "Max processing time is %f" successfulMax
  printfn ""
  printfn "Failed: %d drones" failedDrones.Length
  printfn "Average processing time for failed drones is %f" failedAverage
  printfn "Max processing time for failed drones is %f" failedMax

let private createSwarm swarmName swarmSize func parent = 
  let coordinatorRef = 
    spawnOpt parent (swarmName + "-swarm") <| (fun mailbox -> 
    let timer = new System.Diagnostics.Stopwatch()
    do timer.Start()
    let rec loop state = 
      actor { 
        let! msg = mailbox.Receive()
        //printf "%A" msg
        match msg with
        | Create -> 
          return! loop ([ 1..swarmSize ] |> List.map (fun i -> 
                                              let name = swarmName + "-drone-" + (string i)
                                              let ref = drone name func mailbox
                                              do ref <! Execute
                                              do mailbox.Context.Parent <! DroneReply(StartedEvent name)
                                              name, (ref, Executing)))
        | Finished(status, name, time) -> 
          let newState = 
            (state |> List.map (fun elem -> 
                        match elem with
                        | n, (ref, Executing) when n = name -> 
                          //printfn "Elapsed Time for %s is %f" name time
                          printf "."
                          match status with
                          | Success -> n, (ref, Succeeded time)
                          | Failure message -> n, (ref, Failed(message, time))
                        | n, (ref, status) -> n, (ref, status)))
          
          let leftToProcess = 
            newState
            |> List.filter (fun e -> 
                 let n, (r, s) = e
                 s = Executing)
            |> List.length
          
          match status with
          | Success -> mailbox.Context.Parent <! DroneReply(FinishedEvent(name, time))
          | Failure message -> mailbox.Context.Parent <! DroneReply(FailedEvent(name, time))
          do if (leftToProcess = 0) then mailbox.Self <! Stats
          return! loop newState
        | Stats -> 
          timer.Stop()
          printStats state timer.Elapsed.TotalSeconds
          return! loop state
      }
    loop [])
    <| [ SpawnOption.SupervisorStrategy(Strategy.OneForOne(fun e -> 
                                          match e with
                                          | _ ->                                             
                                            Directive.Stop)) ]
  do coordinatorRef <! Create
  ()

let brains = ref Map.empty

let hiveRef = 
  spawnOpt system ("Hive") <| (fun hiveMailbox -> 
  let event = new Event<Events>()
  let publishedEvent = event.Publish
  let events = ref List.empty
  do publishedEvent.Add(fun x -> events := ([ x ] :: !events))
  let webServerRef = 
    spawn system "webServer" <| fun mailbox -> 
      let echo (webSocket : WebSocket) = 
        let wsSender = 
          MailboxProcessor.Start(fun inbox -> 
            let rec messageLoop = 
              async { 
                let! msg = inbox.Receive()
                let jsonSerializer = FsPickler.CreateJsonSerializer(indent = false)
                let str = jsonSerializer.PickleToString(msg)
                let data = Encoding.UTF8.GetBytes(str)
                let! a = webSocket.send Text data true
                return! messageLoop
              }
            messageLoop)
        
        let notifyLoop = 
          async { 
            while true do
              let! msg = Async.AwaitEvent publishedEvent
              wsSender.Post msg
              return ()
          }
        
        let cts = new CancellationTokenSource()
        Async.Start(notifyLoop, cts.Token)
        fun cx -> 
          socket { 
            let loop = ref true
            while !loop do
              let! msg = webSocket.read()
              match msg with
              | (Text, data, true) -> 
                let jsonSerializer = FsPickler.CreateJsonSerializer(indent = true)
                let str = Encoding.UTF8.GetBytes(jsonSerializer.PickleToString(events))
                do! webSocket.send Text str true
              | (Ping, _, _) -> do! webSocket.send Pong [||] true
              | (Close, _, _) -> 
                do! webSocket.send Close [||] true
                loop := false
              | _ -> ()
          }
      
      let executingAssembly = Assembly.GetExecutingAssembly()
      let srIndex = new StreamReader(executingAssembly.GetManifestResourceStream("index.html"))
      let index = srIndex.ReadToEnd()
      let srBundle = new StreamReader(executingAssembly.GetManifestResourceStream("bundle.js"))
      let bundle = srBundle.ReadToEnd()
      
      let app : WebPart = 
        choose [ path "/websocket" >=> handShake echo
                 //GET >=> choose [ path "/" >=> file "index.html"; browseHome ];
                 GET >=> choose [ path "/" >=> OK index
                                  path "/bundle.js" >=> OK bundle ]
                 NOT_FOUND "Found no handlers." ]
      do startWebServer { defaultConfig with logger = Loggers.ConsoleWindowLogger LogLevel.Warn } app
      let rec loop() = actor { let! msg = mailbox.Receive()
                               return! loop() }
      loop()
  
  let rec loop state = 
    actor { 
      let! msg = hiveMailbox.Receive()
      match msg with
      | CreateSwarm m -> createSwarm m.Name m.Size (!brains).[m.Name] hiveMailbox
      | DroneReply m -> event.Trigger(m)
      return! loop state
    }
  
  loop [])
  <| [ SpawnOption.SupervisorStrategy(Strategy.OneForOne(fun e -> 
                                        match e with
                                        | _ -> Directive.Stop)) ]

let newSwarm swarmName swarmSize (func : unit -> Async<unit>) = 
  let x = 
    CreateSwarm { Name = swarmName
                  Size = swarmSize }
  brains := (!brains).Add(swarmName, func)
  hiveRef <! x
