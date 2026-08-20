import type { Metadata, Viewport } from "next";
import type { ReactNode } from "react";
import { ServiceWorkerRegistration } from "@/components/app-shell/ServiceWorkerRegistration";
import { getInstanceBranding } from "@/lib/branding/server";
import { Providers } from "./providers";
import "./globals.css";

// A function, not a constant: the browser tab and the iOS home-screen label should say whatever the
// host administrator named this instance, and that is only knowable at request time.
export async function generateMetadata(): Promise<Metadata> {
  const { instanceName } = await getInstanceBranding();
  return { ...baseMetadata, title: instanceName, appleWebApp: { ...baseMetadata.appleWebApp, title: instanceName } };
}

const baseMetadata = {
  description: "Planvexa task-management platform shell",
  manifest: "/manifest.webmanifest",
  appleWebApp: {
    capable: true,
    statusBarStyle: "default" as const,
  },
  icons: {
    icon: [
      { url: "/icons/icon-192.png", sizes: "192x192", type: "image/png" },
      { url: "/icons/icon-512.png", sizes: "512x512", type: "image/png" },
    ],
    apple: "/icons/apple-touch-icon.png",
  },
};

export const viewport: Viewport = {
  themeColor: "#2563eb",
};

export default function RootLayout({
  children,
}: {
  children: ReactNode;
}) {
  return (
    <html lang="en" suppressHydrationWarning>
      <body className="min-h-screen bg-background text-foreground antialiased">
        <ServiceWorkerRegistration />
        <Providers>{children}</Providers>
      </body>
    </html>
  );
}
