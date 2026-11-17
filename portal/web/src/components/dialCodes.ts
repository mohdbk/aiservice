/** Country calling codes for the phone field: ISO code and dial code, the Gulf first, then others A to Z. */
export const DIAL_CODES: [string, string][] = [
  ["AE", "971"], ["SA", "966"], ["KW", "965"], ["QA", "974"], ["BH", "973"], ["OM", "968"],
  ["AF", "93"], ["DZ", "213"], ["AR", "54"], ["AU", "61"], ["AT", "43"], ["AZ", "994"], ["BD", "880"], ["BE", "32"], ["BR", "55"],
  ["CA", "1"], ["CN", "86"], ["CY", "357"], ["DK", "45"], ["EG", "20"], ["ET", "251"], ["FI", "358"], ["FR", "33"], ["DE", "49"],
  ["GH", "233"], ["GR", "30"], ["HK", "852"], ["IN", "91"], ["ID", "62"], ["IR", "98"], ["IQ", "964"], ["IE", "353"], ["IT", "39"],
  ["JP", "81"], ["JO", "962"], ["KZ", "7"], ["KE", "254"], ["KR", "82"], ["LB", "961"], ["LY", "218"], ["MY", "60"], ["MV", "960"],
  ["MX", "52"], ["MA", "212"], ["NP", "977"], ["NL", "31"], ["NZ", "64"], ["NG", "234"], ["NO", "47"], ["PK", "92"], ["PS", "970"],
  ["PH", "63"], ["PL", "48"], ["PT", "351"], ["RO", "40"], ["RU", "7"], ["SN", "221"], ["RS", "381"], ["SG", "65"], ["ZA", "27"],
  ["ES", "34"], ["LK", "94"], ["SD", "249"], ["SE", "46"], ["CH", "41"], ["SY", "963"], ["TZ", "255"], ["TH", "66"], ["TN", "216"],
  ["TR", "90"], ["UG", "256"], ["UA", "380"], ["GB", "44"], ["US", "1"], ["UZ", "998"], ["VN", "84"], ["YE", "967"],
];

/** The full number: a number typed with + or 00 is kept as it is; otherwise the dial code goes first (without a leading 0). */
export function fullPhone(dial: string, local: string) {
  const n = local.trim();
  if (/^(\+|00)/.test(n)) return n.replace(/^00/, "+");
  return n ? `+${dial} ${n.replace(/^0+/, "")}` : "";
}
