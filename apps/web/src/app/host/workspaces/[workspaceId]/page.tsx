import { WorkspaceDetailPageClient } from "@/components/host/WorkspaceDetailPageClient";

export const metadata = { title: "Workspace · Host administration" };

export default async function HostWorkspaceDetailPage({
  params,
}: {
  params: Promise<{ workspaceId: string }>;
}) {
  const { workspaceId } = await params;
  return <WorkspaceDetailPageClient workspaceId={workspaceId} />;
}
