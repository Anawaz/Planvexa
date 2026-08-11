import { describe, expect, it } from "vitest";
import { render } from "@testing-library/react";
import { AttachmentPreview } from "./AttachmentPreview";

describe("AttachmentPreview", () => {
  it("renders an img for image/* content types", () => {
    const { container } = render(
      <AttachmentPreview fileName="photo.png" contentType="image/png" href="/proxy/photo.png" />,
    );
    const img = container.querySelector("img");
    expect(img).toHaveAttribute("src", "/proxy/photo.png");
    expect(img).toHaveAttribute("alt", "photo.png");
  });

  it("renders a video[controls] for video/* content types", () => {
    const { container } = render(
      <AttachmentPreview fileName="clip.mp4" contentType="video/mp4" href="/proxy/clip.mp4" />,
    );
    const video = container.querySelector("video");
    expect(video).toHaveAttribute("src", "/proxy/clip.mp4");
    expect(video).toHaveAttribute("controls");
  });

  it("renders an iframe for application/pdf", () => {
    const { container } = render(
      <AttachmentPreview fileName="doc.pdf" contentType="application/pdf" href="/proxy/doc.pdf" />,
    );
    const iframe = container.querySelector("iframe");
    expect(iframe).toHaveAttribute("src", "/proxy/doc.pdf");
    expect(iframe).toHaveAttribute("title", "doc.pdf");
  });

  it("renders nothing for unsupported content types, leaving the download link as the only affordance", () => {
    const { container } = render(
      <AttachmentPreview fileName="archive.zip" contentType="application/zip" href="/proxy/archive.zip" />,
    );
    expect(container).toBeEmptyDOMElement();
  });
});
