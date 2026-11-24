using System;
using System.Reflection;
using ProjectorDisplay;
static class RouterPolicyTests {
    static int Main() {
        var flags=BindingFlags.NonPublic|BindingFlags.Static;
        var settings=typeof(DisplayRouter).GetField("Settings",flags);
        var desired=typeof(DisplayRouter).GetMethod("Desired",flags);
        settings.SetValue(null,new RouterSettings{computerSource=2,systemSource=0});
        var states=new[]{
            new LinkState{playing="1",source=2}, new LinkState{playing="0",source=2},
            new LinkState{playing="unknown",source=2},new LinkState{playing="1",source=1},
            new LinkState{playing="0",source=0},new LinkState{playing="disabled",source=2},null};
        var expected=new[]{"projector","monitor","monitor","monitor","monitor","paused","unknown"};
        for(int i=0;i<states.Length;i++) {
            string actual=(string)desired.Invoke(null,new object[]{states[i]});
            if(actual!=expected[i])throw new Exception("Routing case "+i+": expected "+expected[i]+", got "+actual);
        }
        settings.SetValue(null,new RouterSettings{computerSource=-999,systemSource=0});
        if((string)desired.Invoke(null,new object[]{new LinkState{playing="1",source=2}})!="monitor")
            throw new Exception("Uncalibrated input must not choose the projector.");
        Console.WriteLine("Router input selection: 8 cases passed.");return 0;
    }
}
