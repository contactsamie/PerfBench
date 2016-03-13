module PerfBench.Helpers

open Types

let getAverage list = 
  match list with
  | [] -> 0.0
  | l -> 
    l |> List.averageBy (fun e -> 
           match e with
           | n, (r, Succeeded t) -> t
           | n, (r, Failed(s, t)) -> t)

let getMax list = 
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

let printStats state time = 
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