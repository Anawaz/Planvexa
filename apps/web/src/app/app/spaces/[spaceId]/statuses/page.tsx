import { SpaceStatusPageClient } from "@/components/work/SpaceStatusPageClient";

type SpaceStatusesPageProps = {
  params: Promise<{
    spaceId: string;
  }>;
};

export default async function SpaceStatusesPage({ params }: SpaceStatusesPageProps) {
  const { spaceId } = await params;

  return <SpaceStatusPageClient spaceId={spaceId} />;
}
