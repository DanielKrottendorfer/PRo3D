namespace PRo3D

open System
open System.IO

open Aardvark.Base
open Aardvark.UI
open PRo3D
open OpcViewer.Base

open PRo3D
open PRo3D.Base.Annotation

module Dialogs =    

        
    let onChooseFiles (chosen : list<string> -> 'msg) =
        let cb xs =
            match xs with
                | [] -> chosen []
                | x::[] when x <> null -> x |> Aardvark.Service.Pickler.json.UnPickleOfString |> List.map Aardvark.Service.PathUtils.ofUnixStyle |> chosen
                | _ -> chosen []//failwithf "onChooseFiles: %A" xs
        onEvent "onchoose" [] cb   

    let onChooseDirectory (id:Guid) (chosen : Guid * string -> 'msg) =
        let cb xs =
            match xs with
                | [] -> chosen (id, String.Empty)
                | x::[] when x <> null -> 
                  let id = id
                  let path = 
                    x 
                      |> Aardvark.Service.Pickler.json.UnPickleOfString 
                      |> List.map Aardvark.Service.PathUtils.ofUnixStyle 
                      |> List.tryHead
                  match path with
                  | Some p -> 
                    chosen (id, p)
                  | None -> chosen (id,String.Empty)
                | _ -> chosen (id,String.Empty)//failwithf "onChooseFiles: %A" xs
        onEvent "onchoose" [] cb   

    let onSaveFile (chosen : string -> 'msg) =
        let cb xs =
            match xs with
                | x::[] when x <> null -> x |> Aardvark.Service.Pickler.json.UnPickleOfString |> Aardvark.Service.PathUtils.ofUnixStyle |> chosen
                | _ -> chosen String.Empty //failwithf "onSaveFile: %A" xs
        onEvent "onsave" [] cb

    let onSaveFile1 (chosen : string -> 'msg) (path : Option<string>) =
      let cb xs =
        match path with
        | Some p-> p |> chosen
        | None ->
          match xs with
          | x::[] when x <> null -> 
            x |> Aardvark.Service.Pickler.json.UnPickleOfString |> Aardvark.Service.PathUtils.ofUnixStyle |> chosen
          | _ -> 
            String.Empty |> chosen
      onEvent "onsave" [] cb
          
module Double =
    let degreesFromRadians (d:float) =
        d.DegreesFromRadians()
        
    let radiansFromDegrees (d:float) =
        d.RadiansFromDegrees()
    
module Box3d =
    let extendBy (box:Box3d) (b:Box3d) =
        box.ExtendBy(b)
        box

    let ofSeq (bs:seq<Box3d>) =
        let box = 
            match bs |> Seq.tryHead with
                | Some b -> b
                | None -> failwith "box sequence must not be empty"
                    
        for b in bs do
            box.ExtendBy(b)
        box

module Mod =
    open FSharp.Data.Adaptive

    let bindOption (m : aval<Option<'a>>) (defaultValue : 'b) (project : 'a -> aval<'b>)  : aval<'b> =
        m |> AVal.bind (function | None   -> AVal.constant defaultValue       
                                 | Some v -> project v)

module Copy =
    let rec copyAll' (source : DirectoryInfo) (target : DirectoryInfo) skipExisting =
        
        // Check if the target directory exists, if not, create it.
        if not(Directory.Exists target.FullName) then
            Directory.CreateDirectory target.FullName |> ignore

        // Copy each file into it's new directory.
        for fi in source.GetFiles() do
             let sourceFile = fi.FullName
             let targetFile = Path.Combine(target.FullName, fi.Name)

             if ((sourceFile.ToLower() == targetFile.ToLower()) || (skipExisting && File.Exists(targetFile))) then
                Log.warn "Skipping %s, already exists" targetFile
             else
                Log.line "Copying to %s" targetFile
                fi.CopyTo(Path.Combine((target.ToString()), fi.Name), true) |> ignore      
                
        // Copy each subdirectory using recursion.
        let bla = source.GetDirectories()
        for srcSubDir in bla do
            let nextTgtSubDir = target.CreateSubdirectory(srcSubDir.Name)
            copyAll' srcSubDir nextTgtSubDir skipExisting

    let copyAll source target skipExisting=
        let s = DirectoryInfo(source)
        let t = DirectoryInfo(target)

        copyAll' s t skipExisting

module Sg =
    open FSharp.Data.Adaptive
    open Aardvark.Base.Rendering
    open Aardvark.Rendering.Text    
    open Aardvark.SceneGraph
    open Aardvark.UI
    open FShade

    type UniformScope with
        member x.PointSize : float = uniform?PointSize

    type pointVertex =
        {
            [<Position>] pos : V4d
            [<PointSize>] p : float
            [<Color>] c : V4d
        }
    
    let constantColor (color : V4d) (v : pointVertex) =
        vertex {
            let ps : float = uniform?PointSize
            return { v with c = color; p = ps }
        }

    let pointTrafo (v : pointVertex) =
        vertex {
            let wp = uniform.ModelTrafo * v.pos
            return { 
                v with 
                    pos = uniform.ViewProjTrafo * wp
            }
        }
           
    let computeInvariantScale (view : aval<CameraView>) (near : aval<float>) (p:aval<V3d>) (size:aval<float>) (hfov:aval<float>) =
      adaptive {
          let! p = p
          let! v = view
          let! near = near
          let! size = size
          let! hfov = hfov
          let hfov_rad = Conversion.RadiansFromDegrees(hfov)
         
          let wz = Fun.Tan(hfov_rad / 2.0) * near * size
          let dist = Vec.Distance(p, v.Location)
      
          return ( wz / near ) * dist
      }

    //## LINES ##

    let edgeLines (close : bool) (points : alist<V3d>) (trafo:aval<Trafo3d>) =
      points
        |> AList.map(fun d -> trafo.GetValue().Backward.TransformPos d)
        |> AList.toAVal 
        |> AVal.map (fun l ->
            let list = IndexList.toList l
            let head = list |> List.tryHead
                
            match head with
                | Some h -> if close then list @ [h] else list
                                |> List.pairwise
                                |> List.map (fun (a,b) -> new Line3d(a,b))
                                |> List.toArray
                | None -> [||])       
        
    let composedThickLineShader = 
        Effect.compose [
            toEffect DefaultSurfaces.stableTrafo
            toEffect DefaultSurfaces.vertexColor
            toEffect Shader.ThickLineNew.thickLine
        ]

    let lines (points : alist<V3d>) (offset : aval<float>) (color : aval<C4b>) (width : aval<float>) (trafo : aval<Trafo3d>) = 
      let edges = edgeLines false points trafo
      edges
        |> Sg.lines color
        |> Sg.noEvents
        |> Sg.effect [composedThickLineShader]                             
        |> Sg.trafo trafo
        |> Sg.uniform "LineWidth" width
        |> Sg.uniform "DepthOffset" (
                offset |> AVal.map (fun depthWorld -> depthWorld / (100.0 - 0.1))
           ) 

    let scaledLinesEffect = 
        Effect.compose [
            toEffect Shader.StableTrafo.stableTrafo
            toEffect DefaultSurfaces.vertexColor
            toEffect Shader.ThickLineNew.thickLine
        ]
                               
    let scaledLines (points : alist<V3d>) (color : aval<C4b>) (width : aval<float>) (trafo : aval<Trafo3d>) = 
        let edges = edgeLines false points trafo     
        let size =
            adaptive {
                let! line = edges
                return ((float (line.Length)) * 100.0)
            }
                                                            
        edges
            |> Sg.lines color
            |> Sg.noEvents
            |> Sg.uniform "WorldPos" (trafo |> AVal.map(fun (x : Trafo3d) -> x.Forward.C3.XYZ))
            |> Sg.uniform "Size" size
            |> Sg.effect [scaledLinesEffect]                             
            |> Sg.trafo trafo
            |> Sg.uniform "LineWidth" width             
    
    //## PICKING ##

    let cylinders width positions = 
        positions |> Array.pairwise |> Array.map(fun (a,b) -> Line3d(a,b)) |> Array.map (fun x -> Cylinder3d(x, width))

    let pickable' (pick :aval<Pickable>) (sg: ISg) =
        Sg.PickableApplicator (pick, AVal.constant sg)

    let getTriangles (pos : V3d[]) : array<Triangle3d> =
        let get (ti : int) =
            let i0 = 3 * ti
            Triangle3d(pos.[i0], pos.[i0 + 1], pos.[i0 + 2])


        Array.init (pos.Length / 3) get
    
    //## POINTS ##

    let drawSphere color (size : aval<float>) (pos : aval<V3d>) = 
        Sg.sphere 2 color (AVal.constant 1.0)
            |> Sg.noEvents
            |> Sg.trafo(pos |> AVal.map Trafo3d.Translation)
            |> Sg.uniform "Size" size
            |> Sg.uniform "WorldPos" pos


    let dotEffect = 
        Effect.compose [
            toEffect <| Shader.ScreenSpaceScale.screenSpaceScale
            toEffect <| Shader.StableTrafo.stableTrafo
            toEffect <| DefaultSurfaces.vertexColor
        ]

    let dot (point : aval<V3d>) (size:aval<float>) (color : aval<C4b>) =
        let isgDot = drawSphere color size point
        isgDot
        |> Sg.effect [dotEffect]


    let indexedDotEffect = 
        Effect.compose [
            toEffect Shader.StableTrafo.stableTrafo
            toEffect DefaultSurfaces.vertexColor
            toEffect DefaultSurfaces.pointSprite
        ]

    let indexedGeometryDots (points : alist<V3d>) (size:aval<float>) (color : aval<C4b>) =       
      let points' = points |> AList.toAVal |> AVal.map(fun x -> x |> IndexList.toArray)
      let colors = points' |> AVal.map2(fun c x -> Array.create x.Length c) color
      
      Sg.draw IndexedGeometryMode.PointList
       |> Sg.vertexAttribute DefaultSemantic.Positions points'         
       |> Sg.vertexAttribute DefaultSemantic.Colors colors
       |> Sg.effect [indexedDotEffect]
       |> Sg.uniform "PointSize" size

    let spheresEffect = 
        Effect.compose [ 
            toEffect <| Shader.ScreenSpaceScale.screenSpaceScale
            toEffect <| Shader.StableTrafo.stableTrafo
            toEffect <| DefaultSurfaces.vertexColor 
        ]
    
    let drawSpheres (points : alist<V3d>) (size:aval<float>) (color : aval<C4b>) =
        aset {
            for p in points |> ASet.ofAList do                        
                yield drawSphere color (size) (AVal.constant p)
        } 
        |> Sg.set
        |> Sg.effect [spheresEffect]

    let createSecondaryColor (c : C4b) : C4b = 

       let primary = c.ToC3f().ToHSVf()

       let h = primary.H + 0.3f
       let h = if h >= 1.0f then h - 1.0f else h

       let secondary = HSVf(h, primary.S, primary.V).ToC3f().ToC3b()
       let secondary = C4b(secondary, c.A)       
       Log.line "primary: %A secondary: %A" c secondary
       secondary
       
    let drawPointList (positions : alist<V3d>) (color : aval<C4b>) (pointSize : aval<double>) (offset : aval<double>)= 
        let positions = positions |> AList.toAVal |> AVal.map IndexList.toArray
        let (pointsF, trafo) = PRo3D.Base.Sg.stablePoints' positions

        PRo3D.Base.Sg.drawSingleColorPoints 
            pointsF 
            (color |> AVal.map(fun x -> x.ToC4f().ToV4d()))
            pointSize 
            offset
        |> Sg.trafo trafo
        //failwith ""
                   
    let getDotsIsg (points : alist<V3d>) (size:aval<float>) (color : aval<C4b>) (geometry: aval<Geometry>) (offset : aval<float>) =
        aset {
            let! geometry = geometry
            match geometry with
            | Geometry.Point -> 
                match points|> AList.force |> IndexList.toList |> List.tryHead with
                | Some p -> 
                    yield dot (AVal.constant p) size color
                | _ -> 
                    yield Sg.empty
            | _ -> 
                //let color = color |> AVal.map(fun x -> (x |> createSecondaryColor))
                yield drawPointList points (C4b.VRVisGreen |> AVal.constant) size (offset |> AVal.map(fun x -> x * 1.1))
        } |> Sg.set   

    //## TEXT ##
        
    ///probably move to a shader
    let screenAligned (forw : V3d) (up : V3d) (modelt: Trafo3d) =
        let right = up.Cross forw
        let rotTrafo = 
            new Trafo3d(
                new M44d(
                    right.X, up.X, forw.X, 0.0,
                    right.Y, up.Y, forw.Y, 0.0,
                    right.Z, up.Z, forw.Z, 0.0,
                    0.0,     0.0,  0.0,    1.0
                ),
                new M44d(
                    right.X, right.Y, right.Z, 0.0,
                    up.X,    up.Y,    up.Z,    0.0,
                    forw.X,  forw.Y,  forw.Z,  0.0,
                    0.0,     0.0,     0.0,     1.0
                )
        )
        rotTrafo * modelt

    let stableTextShader = Effect.compose [Shader.StableTrafo.stableTrafo |> toEffect]
    
    let text (view : aval<CameraView>) near hfov pos modelTrafo text (size : aval<double>) =
         
      let invScaleTrafo = computeInvariantScale view near pos size hfov |> AVal.map Trafo3d.Scale           
      
      let billboardTrafo = 
          adaptive {
              let! v = view
              let! modelt = modelTrafo
      
              return screenAligned v.Forward v.Up modelt
          }      
      
      Sg.text (Font.create "Consolas" FontStyle.Regular) C4b.White text
      |> Sg.noEvents
      |> Sg.effect [stableTextShader]        
      |> Sg.trafo invScaleTrafo
      |> Sg.trafo billboardTrafo      
module UI =
    open Aardvark.UI
    open FSharp.Data.Adaptive

    let toAlignmentString (alignment : TTAlignment) =
        match alignment with
        | TTAlignment.Top      -> "top center"
        | TTAlignment.Right    -> "right center"
        | TTAlignment.Bottom   -> "bottom center"   
        | TTAlignment.Left     -> "left center" 
        | _ -> alignment |> sprintf "unknown alignment %A" |> failwith

    let wrapToolTip (text:string) (alignment:TTAlignment ) (dom:DomNode<'a>) : DomNode<'a> =
        //dom
        let attr = 
            [ attribute "title" text
              attribute "data-position" (toAlignmentString alignment) //"top center"
              attribute "data-variation" "mini" ] 
                |> AttributeMap.ofList
                //|> AttributeMap.union dom.                
                
        onBoot "$('#__ID__').popup({inline:true,hoverable:true});" (       
            dom.WithAttributes attr     
        )

    let wrapToolTipRight (text:string) (dom:DomNode<'a>) : DomNode<'a> =
        //dom
        let attr = 
            [ attribute "title" text
              attribute "data-position" "right center"
              attribute "data-variation" "mini"] 
                |> AttributeMap.ofList
                //|> AttributeMap.union dom.Attributes                
                
        onBoot "$('#__ID__').popup({inline:true,hoverable:true});" (       
            dom.WithAttributes attr     
        )

    let wrapToolTipBottom (text:string) (dom:DomNode<'a>) : DomNode<'a> =
        //dom
        let attr = 
            [ attribute "title" text
              attribute "data-position" "bottom center"
              attribute "data-variation" "mini"] 
                |> AttributeMap.ofList
                //|> AttributeMap.union dom.Attributes                
                
        onBoot "$('#__ID__').popup({inline:true,hoverable:true});" (       
            dom.WithAttributes attr     
        )


    let dropDown'' (values : alist<'a>)(selected : aval<Option<'a>>) (change : Option<'a> -> 'msg) (f : 'a ->string)  =

        let attributes (name : string) =
            AttributeMap.ofListCond [
                always (attribute "value" (name))
                onlyWhen (
                        selected 
                            |> AVal.map (
                                fun x -> 
                                    match x with
                                        | Some s -> name = f s
                                        | None   -> name = "-None-"
                            )) (attribute "selected" "selected")
            ]

        let ortisOnChange  = 
            let cb (i : int) =
                let currentState = values.Content |> AVal.force
                change (IndexList.tryAt (i-1) currentState)
                    
            onEvent "onchange" ["event.target.selectedIndex"] (fun x -> x |> List.head |> Int32.Parse |> cb)

        Incremental.select (AttributeMap.ofList [ortisOnChange; style "color:black"]) 
            (
                alist {
                    yield Incremental.option (attributes "-None-") (AList.ofList [text "-None-"])
                    yield! values |> AList.mapi(fun i x -> Incremental.option (attributes (f x)) (AList.ofList [text (f x)]))
                }
            )

module Console =    

    let print (x:'a) : 'a =
        printfn "%A" x
        x

module Lenses = 
    let get    (lens : Lens<'s,'a>) (s:'s) : 'a              = lens.Get(s)
    let set    (lens : Lens<'s,'a>) (v : 'a) (s:'s) : 's     = lens.Set(s,v)
    let set'   (lens : Lens<'s,'a>) (s:'s) (v : 'a)  : 's    = lens.Set(s,v)
    let update (lens : Lens<'s,'a>) (f : 'a->'a) (s:'s) : 's = lens.Update(s,f)



  
module PRo3DNumeric = 
    open FSharp.Data.Adaptive
    open Aardvark.UI

    let inline (=>) a b = Attributes.attribute a b

    type Action = 
        | SetValue of float
        | SetMin of float
        | SetMax of float
        | SetStep of float
        | SetFormat of string

    let update (model : NumericInput) (action : Action) =
        match action with
        | SetValue v -> { model with value = v }
        | SetMin v ->   { model with min = v }
        | SetMax v ->   { model with max = v }
        | SetStep v ->  { model with step = v }
        | SetFormat s -> { model with format = s }

    let formatNumber (format : string) (value : float) =
        String.Format(Globalization.CultureInfo.InvariantCulture, format, value)

    let numericField<'msg> ( f : Action -> seq<'msg> ) ( atts : AttributeMap<'msg> ) ( model : AdaptiveNumericInput ) inputType =         

        let tryParseAndClamp min max fallback (s: string) =
            let parsed = 0.0
            match Double.TryParse(s, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture) with
                | (true,v) -> clamp min max v
                | _ ->  printfn "validation failed: %s" s
                        fallback

        let onWheel' (f : Aardvark.Base.V2d -> seq<'msg>) =
            let serverClick (args : list<string>) : Aardvark.Base.V2d = 
                let delta = List.head args |> Pickler.unpickleOfJson
                delta  / Aardvark.Base.V2d(-100.0,-100.0) // up is down in mouse wheel events

            onEvent' "onwheel" ["{ X: event.deltaX.toFixed(10), Y: event.deltaY.toFixed(10)  }"] (serverClick >> f)

        let attributes = 
            amap {                
                yield style "text-align:right; color : black"                

                let! min = model.min
                let! max = model.max
                let! value = model.value
                match inputType with
                    | Slider ->   
                        yield "type" => "range"
                        yield onChange' (tryParseAndClamp min max value >> SetValue >> f)   // continous updates for slider
                    | InputBox -> 
                        yield "type" => "number"
                        yield onChange' (tryParseAndClamp min max value >> SetValue >> f)  // batch updates for input box (to let user type)

                let! step = model.step
                yield onWheel' (fun d -> value + d.Y * step |> clamp min max |> SetValue |> f)

                yield "step" => sprintf "%f" step
                yield "min"  => sprintf "%f" min
                yield "max"  => sprintf "%f" max

                let! format = model.format
                yield "value" => formatNumber format value
            } 

        Incremental.input (AttributeMap.ofAMap attributes |> AttributeMap.union atts)

    let numericField' = numericField (Seq.singleton) AttributeMap.empty

    let view' (inputTypes : list<NumericInputType>) (model : AdaptiveNumericInput) : DomNode<Action> =
        inputTypes 
            |> List.map (numericField' model) 
            |> List.intersperse (text " ") 
            |> div []

    let view (model : AdaptiveNumericInput) =
        view' [InputBox] model

  module GenericFunctions =
    let rec applyXTimes (a : 'a) (f : 'a -> int -> 'a) (lastIndex : int) =
        match lastIndex with
        | t when t <= 0 -> f a lastIndex
        | t when t > 0 -> applyXTimes (f a lastIndex) f (lastIndex - 1)
        | _ -> a

module Net =
    open System.Threading
    open Aardvark.UI
    let getClient () =
        use cancelToken = new CancellationTokenSource()
        let waitForClient =
            async {
                for i in 1..100 do
                    let wc = new System.Net.WebClient()
                    try
                        let lst = wc.DownloadString("http://localhost:54321/rendering/stats.json")
                        match String.length lst > 3 with
                        | true -> cancelToken.Cancel ()
                        | false -> do! Async.Sleep 1000
                    with ex -> do! Async.Sleep 1000
            }
        try Async.RunSynchronously (waitForClient, -1, cancelToken.Token) with e -> ()
        let wc = new System.Net.WebClient()
        let jsonString = wc.DownloadString("http://localhost:54321/rendering/stats.json")
        let clientStats : list<PRo3D.Base.Utilities.ClientStatistics> =
            Pickler.unpickleOfJson jsonString
        (wc, clientStats)
