import { createBrowserRouter, Navigate } from "react-router";
import { AnalysisPage } from "@/pages/AnalysisPage";
import { DriversPage } from "@/pages/DriversPage";
import { HomePage } from "@/pages/HomePage";
import { ResultsPage } from "@/pages/ResultsPage";
import { TelemetryPage } from "@/pages/TelemetryPage";
import { LivePage } from "@/pages/LivePage";
import { ReplayPage } from "@/pages/ReplayPage";
import { SchedulePage } from "@/pages/SchedulePage";
import { StandingsPage } from "@/pages/StandingsPage";
import { RootLayout } from "@/pages/RootLayout";

/**
 * Live and replay are separate destinations, not tabs over one shared session.
 *
 * They answer different questions and must not be confusable: /live connects to
 * the feed or says nothing is running, and never falls back to an archive;
 * /replay plays nothing until a session is explicitly chosen, and never
 * silently becomes live. A user cannot tell a replay from live by looking at
 * the numbers, so the separation has to be in the URL, not only in a badge.
 */
export const router = createBrowserRouter([
  {
    path: "/",
    element: <RootLayout />,
    children: [
      { index: true, element: <HomePage /> },
      { path: "live", element: <LivePage /> },
      { path: "replay", element: <ReplayPage /> },
      { path: "replay/:year/:meeting/:session", element: <ReplayPage /> },
      { path: "schedule", element: <SchedulePage /> },
      { path: "standings", element: <StandingsPage /> },
      { path: "telemetry", element: <TelemetryPage /> },
      { path: "results", element: <ResultsPage /> },
      { path: "drivers", element: <DriversPage mode="drivers" /> },
      { path: "teams", element: <DriversPage mode="teams" /> },
      { path: "analysis/:year/:meeting/:session", element: <AnalysisPage /> },
      { path: "*", element: <Navigate to="/" replace /> },
    ],
  },
]);
