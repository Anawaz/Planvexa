type ClassDictionary = Record<string, boolean | null | undefined>;
type ClassValue = string | number | false | null | undefined | ClassDictionary;

export function cn(...inputs: ClassValue[]) {
  return inputs
    .flatMap((input) => {
      if (!input) {
        return [];
      }

      if (typeof input === "object") {
        return Object.entries(input)
          .filter(([, enabled]) => Boolean(enabled))
          .map(([className]) => className);
      }

      return String(input);
    })
    .join(" ");
}
