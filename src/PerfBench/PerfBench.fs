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
type private Messages = Execute | Other

let private handleMessage msg func =
        match msg with
            | Execute -> Async.Start (func())
            | _ -> ()

let private funcWithLogging name func = async {
    let timer = new System.Diagnostics.Stopwatch()
    timer.Start()
    printfn "Started %s" name
    let! result = func()
    printfn "Elapsed Time: %f" timer.Elapsed.TotalSeconds
    printfn "Finished %s" name
    result}

let runTest namePrefix numUsers func =
    [1..numUsers] 
        |> List.map (fun i ->         
            let name = namePrefix + (string i)
            let newFunc = fun() -> funcWithLogging name func
            let ref = spawn system name (actorOf (fun msg -> handleMessage msg newFunc)) <! Execute
            ref)

