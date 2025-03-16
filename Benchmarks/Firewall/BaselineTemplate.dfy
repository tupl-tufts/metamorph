include "[File]"

module Baseline {

    import opened Definitions
    import opened Problem

    datatype MethodCall = AddDevice(d:int) | DeleteDevice(d:int, y:int) | AddConnection(s:int, r:int) | DiffDevice(d:int) | MakeCentral(d:int)

    predicate DiffDeviceRequires(database:Database, d:int) 
        reads database
        ensures DiffDeviceRequires(database, d) <==> 
        (exists v :: v in database.D && v != d)
    {
        exists v :: v in database.D && v != d
    }

    method {:testInline [BASELINE_INLINING_DEPTH]} SynthesizeRecursive(s:seq<MethodCall>, database:Database)
    returns (success:bool) 
    modifies database
    {
        if |s| == 0 {
            return true;
        }
        var nextMethodCall := s[0];
        match nextMethodCall {
            case AddDevice(d) => 
                if d !in database.D && database.IsValid() {
                    database.AddDevice(d);
                } else {
                    return false;
                }
            case DiffDevice(d) => 
                if d in database.D && DiffDeviceRequires(database, d) {
                    var _ := database.DiffDevice(d);
                } else {
                    return false;
                }
            case AddConnection(s, r) => 
                if s in database.D && r in database.D && database.IsValid() {
                    database.AddConnection((s, r));
                } else {
                    return false;
                }
            case MakeCentral(d) => 
                if d in database.D  && database.IsValid() {
                    database.MakeCentral(d);
                } else {
                    return false;
                }
            case DeleteDevice(d, y) =>
                if d in database.D && y in database.D && y in database.C && y != d  && database.IsValid() {
                    database.DeleteDevice(d, y);
                } else {
                    return false;
                }
        }
        success := SynthesizeRecursive(s[1..], database);
        return success;
    }

    method {:testEntry} Synthesize(s:seq<MethodCall>) {
        var list := new Database();
        var success := SynthesizeRecursive(s, list);
        if (!success || !Goal(list)) {
            return;
        }
        print("Synthesis goal reached");
    }

}