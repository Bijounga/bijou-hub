// Tests for the calendar time grid's drag gestures and overlap lanes (no UI, no mouse):
//   dotnet run --project tools/gridtest
using BijouHub.Services;
int fails = 0;
void Check(bool ok, string msg) { if (ok) Console.WriteLine("  ok    " + msg); else { Console.WriteLine("  FAIL  " + msg); fails++; } }

// 7 days, 700px wide (100px a day), 60px an hour (1px a minute), snap 15.
var g = new GridGeometry(7, 700, 60);
Check(g.DayAt(0) == 0 && g.DayAt(99.9) == 0 && g.DayAt(100) == 1 && g.DayAt(699) == 6 && g.DayAt(5000) == 6 && g.DayAt(-20) == 0, "pixels map to days (clamped)");
Check(g.MinuteAt(0) == 0 && g.MinuteAt(14) == 0 && g.MinuteAt(15) == 15 && g.MinuteAt(14, Snap.Up) == 15 && g.MinuteAt(8, Snap.Nearest) == 15 && g.MinuteAt(7, Snap.Nearest) == 0, "pixels snap to 15 minutes");
Check(g.MinuteAt(99999) == 1440 && g.MinuteAt(-5) == 0, "minutes clamp to the day");

// ---- Create by dragging: Tue (day 1) from 9:00 to 10:30
var none = new List<GridItem>();
var gest = new TimeGridGesture(g, none);
gest.Begin(150, 9 * 60 + 2);
Check(gest.Move(151, 9 * 60 + 3) == null, "a tiny wobble isn't a drag");
var s = gest.Move(160, 10 * 60 + 25)!;
Check(s.Kind == GestureKind.Create && s.Day == 1 && s.StartMinute == 540 && s.EndMinute == 630, "dragging down makes 9:00-10:30");
var r = gest.End(160, 10 * 60 + 25);
Check(r.Kind == GestureKind.Create && r.StartMinute == 540 && r.EndMinute == 630, "release creates it");
gest.Begin(150, 9 * 60 + 2);
s = gest.Move(150, 8 * 60 + 40)!;
Check(s.StartMinute == 480 + 30 && s.EndMinute == 555, "dragging up from 9:00 makes 8:30-9:15 (anchor cell kept)");
gest.Cancel();
// drag sideways to another day does not change the day
gest.Begin(150, 600); s = gest.Move(450, 660)!; Check(s.Day == 1, "a create drag stays on its day");
gest.Cancel();
// click on empty space: one hour
gest.Begin(250, 14 * 60 + 5);
r = gest.End(250, 14 * 60 + 5);
Check(r.Kind == GestureKind.Create && r.Day == 2 && r.StartMinute == 840 && r.EndMinute == 900, "clicking empty space makes a one-hour block");
gest.Begin(250, 23 * 60 + 50); r = gest.End(250, 23 * 60 + 50);
Check(r.StartMinute == 1380 && r.EndMinute == 1440, "a click near midnight stays inside the day");

// ---- Existing events: move, resize, click
var items = new List<GridItem> { new("e1", 2, 600, 660) , new("e2", 4, 600, 720) }; // Wed 10-11, Fri 10-12
var gi = new TimeGridGesture(g, items);
Check(gi.HitTest(250, 630, out var hit) == GridTarget.Body && hit!.Id == "e1", "the middle of a block is its body");
Check(gi.HitTest(250, 657, out hit) == GridTarget.ResizeHandle && hit!.Id == "e1", "its bottom edge is the resize handle");
Check(gi.HitTest(250, 700, out hit) == GridTarget.Empty, "below it is empty");
Check(gi.HitTest(150, 630, out hit) == GridTarget.Empty, "another day's column is empty");
gi.Begin(250, 630); r = gi.End(250, 630);
Check(r.Kind == GestureKind.Click && r.Id == "e1", "click on a block opens it");
gi.Begin(250, 630); s = gi.Move(250, 690)!; r = gi.End(250, 690);
Check(r.Kind == GestureKind.Move && r.Id == "e1" && r.Day == 2 && r.StartMinute == 660 && r.EndMinute == 720, "dragging a block down an hour moves it (length kept)");
gi.Begin(250, 630); s = gi.Move(450, 630 - 7)!; // sideways two days, up 7px (snaps to 0)
Check(s.Kind == GestureKind.Move && s.Day == 4 && s.StartMinute == 600, "dragging across moves it to another day");
gi.Cancel();
gi.Begin(250, 630); s = gi.Move(250, 10)!; Check(s.StartMinute == 0 && s.EndMinute == 60, "can't be dragged above midnight");
gi.Begin(250, 630); s = gi.Move(250, 5000)!; Check(s.EndMinute == 1440 && s.StartMinute == 1380, "or below it");
gi.Cancel();
gi.Begin(250, 657); s = gi.Move(250, 735)!; r = gi.End(250, 735);
Check(r.Kind == GestureKind.Resize && r.Id == "e1" && r.StartMinute == 600 && r.EndMinute == 735, "dragging the bottom edge resizes it");
gi.Begin(250, 657); s = gi.Move(250, 590)!; Check(s.EndMinute == 615, "it can't shrink below one snap");
gi.Cancel();
gi.Begin(150, 100); gi.Cancel(); r = gi.End(150, 100); Check(r.Kind == GestureKind.None, "a cancelled gesture does nothing");

// ---- Lanes
var lanes = TimeGridLanes.Pack(new List<(int, int)> { (600, 660), (630, 720), (720, 780), (600, 615) });
Check(lanes[0].Lane == 0 && lanes[1].Lane == 1 && lanes[0].Lanes == 2 && lanes[3].Lane == 1 && lanes[1].Lanes == 2, "overlapping blocks take side-by-side lanes");
Check(lanes[2].Lane == 0 && lanes[2].Lanes == 1, "a block after them gets the full width again");
var one = TimeGridLanes.Pack(new List<(int, int)> { (0, 60) });
Check(one[0] == (0, 1), "a lone block is full width");

// ---- Side-by-side hit testing
var shared = new List<GridItem> { new("a", 0, 600, 660, 0, 2), new("b", 0, 600, 660, 1, 2) };
var gs = new TimeGridGesture(g, shared);
gs.HitTest(20, 630, out hit); Check(hit!.Id == "a", "left lane is the first block");
gs.HitTest(80, 630, out hit); Check(hit!.Id == "b", "right lane is the second");

Console.WriteLine(fails == 0 ? "ALL CHECKS PASSED" : $"{fails} FAILED");
