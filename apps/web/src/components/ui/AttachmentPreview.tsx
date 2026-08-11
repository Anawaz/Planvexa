// Inline preview for an attachment's proxy download URL — images and video render directly, PDFs get
// the browser's native viewer in an iframe, everything else falls back to nothing (the caller keeps
// its plain download link regardless, see TaskDetailPanel/CommentItem/ChatPageClient attachment lists).
// Same authenticated-proxy-URL convention as ImageNode.tsx's documentImageHref.
type AttachmentPreviewProps = {
  fileName: string;
  contentType: string;
  href: string;
};

export function AttachmentPreview({ fileName, contentType, href }: AttachmentPreviewProps) {
  if (contentType.startsWith("image/")) {
    return (
      // eslint-disable-next-line @next/next/no-img-element -- authenticated proxy URL, next/image can't fetch it.
      <img
        src={href}
        alt={fileName}
        className="max-h-64 max-w-full rounded-md border border-border object-contain"
      />
    );
  }

  if (contentType.startsWith("video/")) {
    return <video src={href} controls className="max-h-64 max-w-full rounded-md border border-border" />;
  }

  if (contentType === "application/pdf") {
    return <iframe src={href} title={fileName} className="h-64 w-full rounded-md border border-border" />;
  }

  return null;
}
