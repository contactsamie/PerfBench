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

let webServerActor events publishedEvent = 
  spawn system "webServer" <| fun mailbox -> 
    let echo (webSocket : WebSocket) = 
      fun cx -> 
        socket { 
          do publishedEvent |> Observable.subscribe (fun x -> 
                                 let jsonSerializer = FsPickler.CreateJsonSerializer(indent = false)
                                 let str = jsonSerializer.PickleToString(x)
                                 let data = Encoding.UTF8.GetBytes(str)
                                 do webSocket.send Text data true |> Async.RunSynchronously)
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

let private drone name (event : Event<Events>) brain (parent : Actor<CoordinatorMessages>) = 
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
            let! result = Async.Catch(brain())
            //printfn "finished drone %s" name
            //parent.Self <! Finished(name, timer.Elapsed.TotalSeconds)
            timer.Stop()
            match result with
            | Choice1Of2 _ -> return Finished(Success, name, timer.Elapsed.TotalSeconds)
            | Choice2Of2 exn -> return Finished(Failure exn.Message, name, timer.Elapsed.TotalSeconds)
          }
          |!> parent.Self
      }
    loop []

let unleashSwarm swarmName swarmSize func = 
  let coordinatorRef = 
    spawnOpt system (swarmName + "-coordinator") <| (fun mailbox -> 
    let timer = new System.Diagnostics.Stopwatch()
    do timer.Start()
    let event = new Event<Events>()
    let publishedEvent = event.Publish
    let events = ref List.empty
    do publishedEvent |> Observable.subscribe (fun x -> events := ([ x ] :: !events))
    let rec loop state = 
      actor { 
        let! msg = mailbox.Receive()
        //printf "%A" msg
        match msg with
        | Create -> 
          let webServerRef = webServerActor events publishedEvent
          return! loop ([ 1..swarmSize ] |> List.map (fun i -> 
                                              let name = swarmName + "-drone-" + (string i)
                                              //let droneBrains = fun () -> augmentBrain name func mailbox.Self
                                              let ref = drone name event func mailbox
                                              do ref <! Execute
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
          
          do if (leftToProcess = 0) then mailbox.Self <! Stats
          do event.Trigger(FinishedEvent(name, time))
          return! loop newState
        | Stats -> 
          let average = 
            state |> List.averageBy (fun e -> 
                       match e with
                       | n, (r, Succeeded s) -> s
                       | _ -> 0.0)
          
          let _, (_, maxDrone) = 
            state |> List.maxBy (fun e -> 
                       match e with
                       | _, (_, Succeeded t) -> t
                       | _, (_, Failed(s, t)) -> t
                       | _ -> 0.0)
          
          let max = 
            match maxDrone with
            | Succeeded t -> t
            | Failed(s, t) -> t
          
          printfn ""
          printfn "Done processing batch. It took %f seconds total" timer.Elapsed.TotalSeconds
          timer.Stop()
          printfn "Average processing time is %f" average
          printfn "Max processing time is %f" max
          return! loop state
      }
    loop [])
    <| [ SpawnOption.SupervisorStrategy(Strategy.OneForOne(fun e -> 
                                          match e with
                                          | _ -> 
                                            //printfn "failed at parent"
                                            Directive.Stop)) ]
  do coordinatorRef <! Create
  ()
