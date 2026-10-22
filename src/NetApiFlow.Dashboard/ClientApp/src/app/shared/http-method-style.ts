const KNOWN_METHOD_CLASSES: Readonly<Record<string, string>> = {
  GET: 'naf-tag--get',
  POST: 'naf-tag--post',
  PUT: 'naf-tag--put',
  PATCH: 'naf-tag--patch',
  DELETE: 'naf-tag--delete',
};

/** Picks the brand guide's per-verb badge color (Seção 03); unlisted verbs keep the plain tag style. */
export function httpMethodTagClass(method: string): string {
  return KNOWN_METHOD_CLASSES[method.toUpperCase()] ?? '';
}
