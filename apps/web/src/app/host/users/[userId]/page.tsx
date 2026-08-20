import { UserDetailPageClient } from "@/components/host/UserDetailPageClient";

export const metadata = { title: "Account · Host administration" };

export default async function HostUserDetailPage({
  params,
}: {
  params: Promise<{ userId: string }>;
}) {
  const { userId } = await params;
  return <UserDetailPageClient userId={userId} />;
}
