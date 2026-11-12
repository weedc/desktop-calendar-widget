using System;
using System.Collections.Generic;
using System.Linq;
namespace TeamCalendar
{
    static class ConnectionChecks
    {
        static void Check(bool value, string name, List<string> checks) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
        public static void Run(List<string> checks)
        {
            var session = new ReaderLifecycle();
            Check(session.BeginInitial() && !session.BeginInitial(), "only the first connection authorizes creation of a Chrome reader", checks);
            session.Connected(); session.RequireExisting(true);
            Check(!session.BeginInitial() && session.Phase == "hidden", "refresh and navigation reuse the connected hidden reader", checks);
            bool rejected = false; try { session.RequireExisting(false); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && !session.BeginInitial() && session.Phase == "lost", "a missing Chrome window never authorizes an automatic replacement", checks);
            var failed = new ReaderLifecycle(); failed.BeginInitial(); failed.Lost();
            Check(!failed.BeginInitial(), "an unsuccessful initial connection does not create recurring popups", checks);
            failed.Close();
            Check(!failed.BeginInitial() && failed.Phase == "closed", "a disposed reader cannot reopen a browser", checks);
            var cells = Enumerable.Range(1, 28).Select(day => (object)new Dictionary<string, object> { { "label", "일정 없음, 11월 " + day + "일" }, { "events", new object[0] } }).ToArray();
            string raw = "오후 2:30, 가상 미팅, 캘린더: 예시, 위치: 회의실, 2026년 11월 1일";
            var first = (Dictionary<string, object>)cells[0]; first["label"] = "일정 1개, 11월 1일"; first["events"] = new object[] { raw, raw };
            var snapshot = new Dictionary<string, object> { { "month", "2026-11" }, { "cells", cells } };
            var source = new CalendarSource { id = "demo@example.invalid", name = "예시", color = "#487F91" };
            var parsed = CalendarReader.Parse(snapshot, new DateTime(2026, 11, 1), source);
            Check(parsed.events.Count == 1 && parsed.events[0].title == "가상 미팅" && parsed.events[0].sort == 870 && parsed.events[0].location == "회의실" && parsed.events[0].uid == "DBD6656405DA940F708D", "accessible labels retain stable occurrence IDs, locations, time parsing and deduplication", checks);
            first["label"] = "일정 3개, 11월 1일"; rejected = false;
            try { CalendarReader.Parse(snapshot, new DateTime(2026, 11, 1), source); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "an incomplete overflow read fails instead of replacing cached events", checks);
        }
    }
}
