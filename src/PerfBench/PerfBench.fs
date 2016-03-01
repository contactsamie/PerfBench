module PerfBench.Engine

open Akka.FSharp
open Akka.Configuration
open System
open HttpClient
open FSharp.Data
open FSharp.Data.JsonExtensions

// initialize
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

//
type private Messages = Execute
type private CoordinatorMessages = Create | Finished of string*float | Stats
type private TaskStatus = Executing | Succeeded of float | Failed of string

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
            let rec loop state =
                actor {
                    let! msg = mailbox.Receive()                    
                    match msg with
                        | Create -> return! loop ([1..numUsers] 
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
                                                                        printfn "Elapsed Time for %s is %f" name time
                                                                        n,(ref,Succeeded time)
                                                                    | n,(ref,status) -> n,(ref,status)
                                                            ))
                                                    let leftToProcess = newState |> List.filter (fun e -> 
                                                                                                let n,(r,s) = e
                                                                                                s = Executing)
                                                                                 |> List.length
                                                    do if (leftToProcess = 0) then mailbox.Self <! Stats
                                                    return! loop newState
                        | Stats -> 
                                let average = state |> List.averageBy (fun e -> 
                                                        match e with
                                                            | n,(r,Succeeded s) -> s
                                                            | _ -> 0.0                                                        
                                                        )
                                printf "Done with average time of %f" average
                                return! loop state
                }
            loop []
    do coordinatorRef <! Create    
    ()    
