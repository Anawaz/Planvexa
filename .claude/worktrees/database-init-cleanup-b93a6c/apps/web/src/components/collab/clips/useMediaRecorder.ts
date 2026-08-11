"use client";

import { useCallback, useRef, useState } from "react";

export type RecordingKind = "screen" | "camera" | "audio";

/**
 * Browser-side recording via the standard MediaRecorder API — no backend recording
 * infrastructure. The browser captures screen/camera/mic, produces a Blob when stopped, and the caller
 * uploads it through the normal Clip upload endpoint (see ClipsPageClient) like any other file.
 */
export function useMediaRecorder() {
  const [isRecording, setIsRecording] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const recorderRef = useRef<MediaRecorder | null>(null);
  const chunksRef = useRef<Blob[]>([]);
  const streamRef = useRef<MediaStream | null>(null);
  const startedAtRef = useRef<number>(0);

  const stopTracks = useCallback(() => {
    streamRef.current?.getTracks().forEach((track) => track.stop());
    streamRef.current = null;
  }, []);

  const start = useCallback(async (kind: RecordingKind) => {
    setError(null);
    try {
      const stream =
        kind === "screen"
          ? await navigator.mediaDevices.getDisplayMedia({ video: true, audio: true })
          : kind === "camera"
            ? await navigator.mediaDevices.getUserMedia({ video: true, audio: true })
            : await navigator.mediaDevices.getUserMedia({ audio: true });

      streamRef.current = stream;
      chunksRef.current = [];
      const recorder = new MediaRecorder(stream);
      recorder.ondataavailable = (event) => {
        if (event.data.size > 0) chunksRef.current.push(event.data);
      };
      recorder.onstop = () => stopTracks();
      recorder.start();
      recorderRef.current = recorder;
      startedAtRef.current = Date.now();
      setIsRecording(true);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not start recording.");
    }
  }, [stopTracks]);

  const stop = useCallback((): Promise<{ blob: Blob; durationSeconds: number } | null> => {
    return new Promise((resolve) => {
      const recorder = recorderRef.current;
      if (!recorder) {
        resolve(null);
        return;
      }

      recorder.onstop = () => {
        stopTracks();
        const blob = new Blob(chunksRef.current, { type: recorder.mimeType || "video/webm" });
        chunksRef.current = [];
        setIsRecording(false);
        resolve({ blob, durationSeconds: (Date.now() - startedAtRef.current) / 1000 });
      };
      recorder.stop();
    });
  }, [stopTracks]);

  return { isRecording, error, start, stop };
}
