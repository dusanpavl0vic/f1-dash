import { useParams } from "react-router";
import { AnalysisView } from "@/features/analysis/components/AnalysisView";

/**
 * Analysis for a session, addressable by URL.
 *
 * The route parameters exist so a specific session's analysis is shareable and
 * survives a reload; the view itself reads whatever the backend has loaded,
 * which the replay page has already put in place.
 */
export function AnalysisPage() {
  const { year, meeting, session } = useParams();

  return (
    <AnalysisView
      active
      key={`${year}-${meeting}-${session}`}
    />
  );
}
