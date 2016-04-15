module Program

open PerfBench.Engine
open FSharp.Data
open System.Threading.Tasks

[<EntryPoint>]
let main argv = 
    let rnd = System.Random()
    
    let db = 
        if argv.Length > 0 then argv.[0]
        else ""
    
    let totalAgents = 
        if argv.Length > 1 then (int argv.[1])
        else 0
    
    let getRandomNumberAsString max = string (rnd.Next(max))
    let getRandomNumber max = rnd.Next(max)

    let doStuff() = 
        async { 
            do! getRandomNumber 2000 |> Async.Sleep
            ()
        }
    
    let doStuffThatFails() = 
        async { 
            do! getRandomNumber 2000
                |> Task.Delay
                |> Async.AwaitTask
            if (getRandomNumber (2) > 0) then failwith ("fail")
            ()
        }
    
    do newSwarm "test" 500 [ doStuffThatFails; doStuff; doStuff; doStuffThatFails ]
    do System.Diagnostics.Process.Start("http://localhost:8083") |> ignore
    System.Console.ReadLine() |> ignore
    0
