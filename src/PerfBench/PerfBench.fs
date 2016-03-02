module PerfBench.Engine

open Akka.FSharp
open Akka.Configuration
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
let private config2 = @"";

let private system = System.create "MySystem" <| Configuration.parse(config2)

// Load testing functionality
type private Messages = Execute
type private CoordinatorMessages = Create | Finished of string*float | Stats
type private TaskStatus = Executing | Succeeded of float | Failed of string
type private Events = FinishedEvent of string*float

let private funcWithLogging name func parent = async {
  let timer = new System.Diagnostics.Stopwatch()
  timer.Start()
  let! result = func()    
  parent <! Finished (name,timer.Elapsed.TotalSeconds)
  timer.Stop()
  result}

let runTest namePrefix numUsers func =
  let coordinatorRef =
    spawn system "Coordinator" <| fun mailbox ->
      let event = new Event<'t>()
      let publishedEvent = event.Publish
      let events = ref List.empty
      do publishedEvent |> Observable.subscribe (fun x -> events := ([x] :: !events))
      let rec loop state =
        actor {
          let! msg = mailbox.Receive()                    
          match msg with
            | Create -> 
              let webServerRef = spawn system "webServer" <| fun mailbox ->
                let echo (webSocket : WebSocket) =
                  fun cx -> socket {
                    do publishedEvent |> Observable.subscribe (fun x -> 
                                                                let jsonSerializer = FsPickler.CreateJsonSerializer(indent = true)
                                                                let str = Encoding.UTF8.GetBytes(jsonSerializer.PickleToString (x))
                                                                do webSocket.send Text str true |> Async.StartAsTask                                                                
                                                                ) |> ignore                    
                    let loop = ref true
                    while !loop do
                      let! msg = webSocket.read()
                      match msg with
                      | (Text, data, true) ->                        
                        let jsonSerializer = FsPickler.CreateJsonSerializer(indent = true)
                        let str = Encoding.UTF8.GetBytes(jsonSerializer.PickleToString (events))
                        do! webSocket.send Text str true
                      | (Ping, _, _) ->
                        do! webSocket.send Pong [||] true
                      | (Close, _, _) ->
                        do! webSocket.send Close [||] true
                        loop := false
                      | _ -> ()
                  }
                
                let executingAssembly = Assembly.GetExecutingAssembly()
                let sr = new StreamReader(executingAssembly.GetManifestResourceStream("index.html"));
                let index = sr.ReadToEnd()
                
                let app : WebPart =
                  choose [
                    path "/websocket" >=> handShake echo
                    //GET >=> choose [ path "/" >=> file "index.html"; browseHome ];
                    GET >=> choose [ path "/" >=> OK index ];
                    NOT_FOUND "Found no handlers."
                    ]
  
                do startWebServer { defaultConfig with logger = Loggers.ConsoleWindowLogger LogLevel.Warn } app
                let rec loop() =
                    actor { 
                        let! msg = mailbox.Receive()
                        return! loop()                                                                                            
                    }
                loop()
              return! loop ([1..numUsers] 
                |> List.map (fun i ->         
                  let name = namePrefix + (string i)                                                                                                                        
                  let newFunc = fun() -> funcWithLogging name func mailbox.Self
                  let ref = spawn system name <| fun mailbox ->
                    let rec loop state =
                        actor { 
                            let! msg = mailbox.Receive()
                            match msg with
                              | Execute -> Async.Start (newFunc())                                                                                                    
                        }
                    loop []
                  do ref <! Execute                                                            
                  name,(ref,Executing)))
            | Finished (name, time) -> 
                let newState = (state
                    |> List.map (fun elem ->
                    match elem with
                      | n,(ref,Executing) when n = name -> 
                          //printfn "Elapsed Time for %s is %f" name time
                          printf "."
                          n,(ref,Succeeded time)
                      | n,(ref,status) -> n,(ref,status)
                    ))
                let leftToProcess = newState |> List.filter (fun e -> 
                                                            let n,(r,s) = e
                                                            s = Executing)
                                             |> List.length
                do if (leftToProcess = 0) then mailbox.Self <! Stats
                do event.Trigger(FinishedEvent (name,time))
                //events := [FinishedEvent (name,time)] :: !events
                return! loop newState
            | Stats -> 
                let average = state |> List.averageBy (fun e -> 
                                        match e with
                                            | n,(r,Succeeded s) -> s
                                            | _ -> 0.0                                                        
                                        )
                let n,(r,Succeeded max) = state |> List.maxBy (fun e -> 
                                        match e with
                                            | n,(r,Succeeded s) -> s
                                            | _ -> 0.0
                                        )
                printfn ""
                printfn "Done processing batch."
                printfn "Average processing time is %f" average
                printfn "Max processing time is %f" max
                //mystate := state
                return! loop state
        }
      loop []
  do coordinatorRef <! Create
  ()    
