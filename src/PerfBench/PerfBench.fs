module PerfBench.Engine

open Akka.Actor
open Akka.FSharp
open System.Text
open System.Reflection
open System.IO
open Nessos.FsPickler.Json
open Suave
open Suave.Http
open Suave.Operators
open Suave.Filters
open Suave.Successful
open Suave.RequestErrors
open Suave.Logging
open Suave.Sockets.Control
open Suave.WebSocket
open System.Threading
open Types
open Helpers

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
let private system = System.create "HiveSystem" <| Configuration.parse (config2)
let private brains = ref Map.empty
let private event = new Event<Events>()
let private publishedEvent = event.Publish
let private events = ref List.empty

let private spawnWeb parent = 
    spawn parent "webServer" <| fun mailbox -> 
        let webServer (webSocket : WebSocket) = 
            let wsSender = 
                MailboxProcessor.Start(fun inbox -> 
                    let rec messageLoop = 
                        async { 
                            let! msg = inbox.Receive()
                            let jsonSerializer = FsPickler.CreateJsonSerializer(indent = false)
                            let str = jsonSerializer.PickleToString(msg)
                            let data = Encoding.UTF8.GetBytes(str)
                            let! a = webSocket.send Text data true
                            //printf ">"
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
                        //printf "%A" msg
                        match msg with                       
                        | (Text, data, true) -> 
                            let jsonSerializer = FsPickler.CreateJsonSerializer(indent = false)
                            let str = Encoding.UTF8.GetBytes(jsonSerializer.PickleToString(events))
                            do! webSocket.send Text str true
                            //()
                        | (Ping, _, _) -> do! webSocket.send Pong [||] true
                        | (Close, _, _) -> 
                            do! webSocket.send Close [||] true
                            do cts.Cancel()
                            loop := false
                        | _ -> ()
                }
        
        let executingAssembly = Assembly.GetExecutingAssembly()
        let srIndex = new StreamReader(executingAssembly.GetManifestResourceStream("index.html"))
        let index = srIndex.ReadToEnd()
        let srBundle = new StreamReader(executingAssembly.GetManifestResourceStream("bundle.js"))
        let bundle = srBundle.ReadToEnd()
        
        let app : WebPart = 
            choose [ path "/websocket" >=> handShake webServer
                     //GET >=> choose [ path "/" >=> file "index.html"; browseHome ];
                     GET >=> choose [ path "/" >=> OK index
                                      path "/bundle.js" >=> OK bundle ]
                     NOT_FOUND "Found no handlers." ]
        do startWebServer { defaultConfig with logger = Loggers.ConsoleWindowLogger LogLevel.Warn } app
        let rec loop() = actor { let! msg = mailbox.Receive()
                                 return! loop() }
        loop()

let private spawnDrone name (brain:(unit -> Async<unit>) list) (parent : Actor<CoordinatorMessages>) = 
    spawn parent name <| fun droneMailbox -> 
        //printfn "starting drone %s" name
        let timer = new System.Diagnostics.Stopwatch()
        do droneMailbox.Defer(fun () -> 
               printfn "failed %s" name
               parent.Self <! Finished(Failure(name, "Unknown Error", timer.Elapsed.TotalSeconds)))
        let rec loop() = 
            actor { 
                let! msg = droneMailbox.Receive()
                //printf "%A" msg
                match msg with
                | Execute x -> 
                    async { 
                        timer.Start()
                        let! result = brain.[x]() |> Async.Catch
                        timer.Stop()
                        match result with
                        | Choice1Of2 _ -> return Finished(Success(name, "", timer.Elapsed.TotalSeconds))
                        | Choice2Of2 exn -> return Finished(Failure(name, exn.Message, timer.Elapsed.TotalSeconds))
                    }
                    |!> parent.Self
            }
        loop()

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
                                                          let ref = spawnDrone name func mailbox
                                                          do ref <! Execute 0
                                                          printf "."
                                                          do mailbox.Context.Parent <! DroneReply(StartedEvent name)
                                                          name, ref, Executing))
                | Finished result -> 
                    let newState = 
                        match result with
                        | Success(droneName, message, time) -> 
                            (state |> List.map (fun elem -> 
                                          match elem with
                                          | name, ref, Executing when name = droneName -> 
                                              printf "v"
                                              (name, ref, Succeeded(message, time))
                                          | name, ref, state -> name, ref, state))
                        | Failure(droneName, message, time) -> 
                            //printfn "failed %s %s" droneName message
                            (state |> List.map (fun elem -> 
                                          match elem with
                                          | name, ref, Executing when name = droneName -> 
                                              printf "x"
                                              (name, ref, Failed(message, time))
                                          | name, ref, state -> name, ref, state))
                    
                    let unfinished = 
                        newState
                        |> List.filter (fun e -> 
                               let n, r, s = e
                               s = Executing)
                        |> List.length
                    
                    match result with
                    | Success(name, message, time) -> mailbox.Context.Parent <! DroneReply(FinishedEvent(name, message, time))
                    | Failure(name, message, time) -> mailbox.Context.Parent <! DroneReply(FailedEvent(name, message, time))
                    do if (unfinished = 0) then mailbox.Self <! Stats
                    return! loop newState
                | Stats -> 
                    timer.Stop()
                    printStats state timer.Elapsed.TotalSeconds
                    return! loop state
            }
        loop [])
        <| [ SpawnOption.SupervisorStrategy(Strategy.OneForOne(fun e -> 
                                                match e with
                                                | _ -> Directive.Stop)) ]
    do coordinatorRef <! Create
    ()

let private hiveRef = 
    spawnOpt system ("Hive") <| (fun hiveMailbox -> 
    let webRef = spawnWeb hiveMailbox
    do publishedEvent.Add(fun x -> events := ([ x ] :: !events))
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

// actual api
let newSwarm swarmName swarmSize (func : (unit -> Async<unit>) list) = 
    let x = 
        CreateSwarm { Name = swarmName
                      Size = swarmSize }
    brains := (!brains).Add(swarmName, func)
    hiveRef <! x
