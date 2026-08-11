"use client";

import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { search, type SearchResult } from "./client";

/** Backend ignores shorter terms; matching the floor here keeps the query from firing at all. */
const MIN_TERM_LENGTH = 2;
const DEBOUNCE_MS = 250;

/** Debounced global search. Idle until the trimmed term reaches two characters. */
export function useSearch(term: string) {
  const trimmed = term.trim();
  const [debounced, setDebounced] = useState(trimmed);

  useEffect(() => {
    const handle = window.setTimeout(() => setDebounced(trimmed), DEBOUNCE_MS);
    return () => window.clearTimeout(handle);
  }, [trimmed]);

  return useQuery<SearchResult[]>({
    queryKey: ["search", debounced],
    queryFn: () => search(debounced),
    enabled: debounced.length >= MIN_TERM_LENGTH,
    staleTime: 30_000,
  });
}
