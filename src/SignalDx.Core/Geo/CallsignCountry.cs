namespace SignalDx.Geo;

/// <summary>
/// Offline DXCC-style country from callsign prefix (longest match).
/// Good enough for Band Activity labels; not a full ITU allocation table.
/// </summary>
public static class CallsignCountry
{
    // Longest prefixes first within each group; lookup sorts by length.
    static readonly (string Prefix, string Country)[] Table =
    [
        // Special / compound first
        ("3DA0", "Eswatini"), ("3DA", "Eswatini"), ("3B6", "Agalega"), ("3B7", "St Brandon"),
        ("3B8", "Mauritius"), ("3B9", "Rodriguez"), ("3B", "Mauritius"),
        ("3D2", "Fiji"), ("3G", "Chile"), ("3V", "Tunisia"), ("3W", "Vietnam"), ("3X", "Guinea"),
        ("3Y", "Bouvet"), ("3A", "Monaco"), ("3C", "Eq Guinea"), ("3Z", "Poland"),

        ("4K", "Azerbaijan"), ("4L", "Georgia"), ("4O", "Montenegro"), ("4S", "Sri Lanka"),
        ("4U1I", "ITU Geneva"), ("4U1U", "UN NY"), ("4U", "UN"), ("4X", "Israel"), ("4Z", "Israel"),
        ("5A", "Libya"), ("5B", "Cyprus"), ("5H", "Tanzania"), ("5N", "Nigeria"), ("5R", "Madagascar"),
        ("5T", "Mauritania"), ("5U", "Niger"), ("5V", "Togo"), ("5W", "Samoa"), ("5X", "Uganda"),
        ("5Z", "Kenya"), ("6W", "Senegal"), ("6Y", "Jamaica"), ("7O", "Yemen"), ("7P", "Lesotho"),
        ("7Q", "Malawi"), ("7X", "Algeria"), ("8P", "Barbados"), ("8Q", "Maldives"), ("8R", "Guyana"),
        ("9A", "Croatia"), ("9G", "Ghana"), ("9H", "Malta"), ("9J", "Zambia"), ("9K", "Kuwait"),
        ("9L", "Sierra Leone"), ("9M2", "W Malaysia"), ("9M6", "E Malaysia"), ("9M8", "E Malaysia"),
        ("9M", "Malaysia"),         ("9N", "Nepal"), ("9Q", "DR Congo"), ("9V", "Singapore"), ("9Y", "Trinidad"),

        ("A2", "Botswana"), ("A3", "Tonga"), ("A4", "Oman"), ("A5", "Bhutan"), ("A6", "UAE"),
        ("A7", "Qatar"), ("A9", "Bahrain"), ("AP", "Pakistan"), ("BV", "Taiwan"), ("BY", "China"),
        ("B", "China"),

        ("C2", "Nauru"), ("C3", "Andorra"), ("C5", "Gambia"), ("C6", "Bahamas"), ("C9", "Mozambique"),
        ("CE0", "Easter Is"), ("CE9", "Antarctica"), ("CE", "Chile"), ("CN", "Morocco"),
        ("CO", "Cuba"), ("CP", "Bolivia"), ("CT3", "Madeira"), ("CT", "Portugal"), ("CU", "Azores"),
        ("CX", "Uruguay"), ("CY0", "Sable Is"), ("CY9", "St Paul"), ("C", "Portugal"),

        ("D2", "Angola"), ("D4", "Cape Verde"), ("D6", "Comoros"), ("DL", "Germany"), ("DM", "Germany"),
        ("DN", "Germany"), ("DO", "Germany"), ("DP", "Germany"), ("DQ", "Germany"), ("DR", "Germany"),
        ("DU", "Philippines"), ("E2", "Thailand"), ("E3", "Eritrea"), ("E4", "Palestine"),
        ("E5", "S Cook"), ("E6", "Niue"), ("E7", "Bosnia"), ("EA6", "Balearic"), ("EA8", "Canary"),
        ("EA9", "Ceuta/Melilla"), ("EA", "Spain"), ("EI", "Ireland"), ("EK", "Armenia"),
        ("EL", "Liberia"), ("EP", "Iran"), ("ER", "Moldova"), ("ES", "Estonia"), ("ET", "Ethiopia"),
        ("EU", "Belarus"), ("EX", "Kyrgyzstan"), ("EY", "Tajikistan"), ("EZ", "Turkmenistan"),

        ("F2", "Guadeloupe"), ("FG", "Guadeloupe"), ("FH", "Mayotte"), ("FJ", "St Barthelemy"),
        ("FK", "New Caledonia"), ("FM", "Martinique"), ("FO", "Fr Polynesia"), ("FP", "St Pierre"),
        ("FR", "Reunion"), ("FS", "St Martin"), ("FT", "French Ant"), ("FW", "Wallis"),
        ("FY", "Fr Guiana"), ("F", "France"),

        ("G", "England"), ("GD", "Isle of Man"), ("GI", "N Ireland"), ("GJ", "Jersey"),
        ("GM", "Scotland"), ("GU", "Guernsey"), ("GW", "Wales"), ("M", "England"),
        ("MD", "Isle of Man"), ("MI", "N Ireland"), ("MJ", "Jersey"), ("MM", "Scotland"),
        ("MU", "Guernsey"), ("MW", "Wales"), ("2E", "England"), ("2D", "Isle of Man"),
        ("2I", "N Ireland"), ("2J", "Jersey"), ("2M", "Scotland"), ("2U", "Guernsey"), ("2W", "Wales"),

        ("H4", "Solomon"), ("HA", "Hungary"), ("HB0", "Liechtenstein"), ("HB", "Switzerland"),
        ("HC8", "Galapagos"), ("HC", "Ecuador"), ("HH", "Haiti"), ("HI", "Dominican Rep"),
        ("HK0", "San Andres"), ("HK", "Colombia"), ("HL", "S Korea"), ("HP", "Panama"),
        ("HR", "Honduras"), ("HS", "Thailand"), ("HV", "Vatican"), ("HZ", "Saudi Arabia"),

        ("I", "Italy"), ("IS0", "Sardinia"), ("IT9", "Sicily"), ("J2", "Djibouti"),
        ("J3", "Grenada"), ("J5", "Guinea-Bissau"), ("J6", "St Lucia"), ("J7", "Dominica"),
        ("J8", "St Vincent"), ("JA", "Japan"), ("JD1", "Ogasawara"), ("JE", "Japan"), ("JF", "Japan"),
        ("JG", "Japan"), ("JH", "Japan"), ("JI", "Japan"), ("JJ", "Japan"), ("JK", "Japan"),
        ("JL", "Japan"), ("JM", "Japan"), ("JN", "Japan"), ("JO", "Japan"), ("JP", "Japan"),
        ("JQ", "Japan"), ("JR", "Japan"), ("JS", "Japan"), ("JT", "Mongolia"), ("JW", "Svalbard"),
        ("JX", "Jan Mayen"), ("JY", "Jordan"),

        ("K", "USA"), ("N", "USA"), ("W", "USA"), ("AA", "USA"), ("AB", "USA"), ("AC", "USA"),
        ("AD", "USA"), ("AE", "USA"), ("AF", "USA"), ("AG", "USA"), ("AH", "USA"), ("AI", "USA"),
        ("AJ", "USA"), ("AK", "USA"), ("AL", "Alaska"), ("KL", "Alaska"), ("NL", "Alaska"), ("WL", "Alaska"),
        ("KP1", "Navassa"), ("KP2", "USVI"), ("KP3", "Puerto Rico"), ("KP4", "Puerto Rico"),
        ("KP5", "Desecheo"), ("KH0", "Mariana"), ("KH1", "Baker"), ("KH2", "Guam"),
        ("KH3", "Johnston"), ("KH4", "Midway"), ("KH5", "Palmyra"), ("KH6", "Hawaii"),
        ("KH7", "Hawaii"), ("KH8", "Am Samoa"), ("KH9", "Wake"), ("KG4", "Guantanamo"),

        ("LA", "Norway"), ("LB", "Norway"), ("LC", "Norway"), ("LD", "Norway"), ("LE", "Norway"),
        ("LF", "Norway"), ("LG", "Norway"), ("LH", "Norway"), ("LI", "Norway"), ("LJ", "Norway"),
        ("LK", "Norway"), ("LL", "Norway"), ("LM", "Norway"), ("LN", "Norway"),
        ("LO", "Argentina"), ("LP", "Argentina"), ("LQ", "Argentina"), ("LR", "Argentina"),
        ("LS", "Argentina"), ("LT", "Argentina"), ("LU", "Argentina"), ("LV", "Argentina"),
        ("LW", "Argentina"), ("LX", "Luxembourg"), ("LY", "Lithuania"), ("LZ", "Bulgaria"),

        ("OA", "Peru"), ("OD", "Lebanon"), ("OE", "Austria"), ("OH0", "Aland"), ("OH", "Finland"),
        ("OJ0", "Market Reef"), ("OK", "Czech Rep"), ("OL", "Czech Rep"), ("OM", "Slovakia"),
        ("ON", "Belgium"), ("OX", "Greenland"), ("OY", "Faroe"), ("OZ", "Denmark"),

        ("P2", "Papua NG"), ("P4", "Aruba"), ("PA", "Netherlands"), ("PB", "Netherlands"),
        ("PC", "Netherlands"), ("PD", "Netherlands"), ("PE", "Netherlands"), ("PF", "Netherlands"),
        ("PG", "Netherlands"), ("PH", "Netherlands"), ("PI", "Netherlands"), ("PJ2", "Curacao"),
        ("PJ4", "Bonaire"), ("PJ5", "St Eustatius"), ("PJ6", "Saba"), ("PJ7", "St Maarten"),
        ("PK", "Indonesia"), ("PP", "Brazil"), ("PQ", "Brazil"), ("PR", "Brazil"), ("PS", "Brazil"),
        ("PT", "Brazil"), ("PU", "Brazil"), ("PV", "Brazil"), ("PW", "Brazil"), ("PX", "Brazil"),
        ("PY", "Brazil"), ("PZ", "Suriname"),

        ("R", "Russia"), ("UA", "Russia"), ("UB", "Russia"), ("UC", "Russia"), ("UD", "Russia"),
        ("UE", "Russia"), ("UF", "Russia"), ("UG", "Russia"), ("UH", "Russia"), ("UI", "Russia"),
        ("RA", "Russia"), ("RB", "Russia"), ("RC", "Russia"), ("RD", "Russia"), ("RE", "Russia"),
        ("RF", "Russia"), ("RG", "Russia"), ("RH", "Russia"), ("RI", "Russia"), ("RJ", "Russia"),
        ("RK", "Russia"), ("RL", "Russia"), ("RM", "Russia"), ("RN", "Russia"), ("RO", "Russia"),
        ("RP", "Russia"), ("RQ", "Russia"), ("RR", "Russia"), ("RS", "Russia"), ("RT", "Russia"),
        ("RU", "Russia"), ("RV", "Russia"), ("RW", "Russia"), ("RX", "Russia"), ("RY", "Russia"),
        ("RZ", "Russia"), ("UA9", "Asiatic Russia"), ("UA0", "Asiatic Russia"),
        ("UN", "Kazakhstan"), ("UK", "Uzbekistan"), ("UY", "Ukraine"), ("UR", "Ukraine"),
        ("US", "Ukraine"), ("UT", "Ukraine"), ("UU", "Ukraine"), ("UV", "Ukraine"), ("UW", "Ukraine"),
        ("UX", "Ukraine"), ("UZ", "Ukraine"),

        ("S0", "W Sahara"), ("S2", "Bangladesh"), ("S5", "Slovenia"), ("S7", "Seychelles"),
        ("S9", "Sao Tome"), ("SM", "Sweden"), ("SP", "Poland"), ("ST", "Sudan"), ("SU", "Egypt"),
        ("SV5", "Dodecanese"), ("SV9", "Crete"), ("SV", "Greece"), ("SY", "Greece"),

        ("T2", "Tuvalu"), ("T30", "W Kiribati"), ("T31", "C Kiribati"), ("T32", "E Kiribati"),
        ("T33", "Banaba"), ("T7", "San Marino"), ("T8", "Palau"), ("TA", "Turkey"),
        ("TF", "Iceland"), ("TG", "Guatemala"), ("TI", "Costa Rica"), ("TI9", "Cocos"),
        ("TJ", "Cameroon"), ("TK", "Corsica"), ("TL", "C Africa"), ("TN", "Congo"),
        ("TR", "Gabon"), ("TT", "Chad"), ("TU", "Ivory Coast"), ("TY", "Benin"), ("TZ", "Mali"),

        ("UA", "Russia"), ("V2", "Antigua"), ("V3", "Belize"), ("V4", "St Kitts"), ("V5", "Namibia"),
        ("V6", "Micronesia"), ("V7", "Marshall Is"), ("V8", "Brunei"), ("VE", "Canada"),
        ("VA", "Canada"), ("VO", "Canada"), ("VY", "Canada"), ("VK0", "Heard/Macquarie"),
        ("VK9", "Australia"), ("VK", "Australia"), ("VP2E", "Anguilla"), ("VP2M", "Montserrat"),
        ("VP2V", "Brit Virgin"), ("VP5", "Turks Caicos"), ("VP6", "Pitcairn"), ("VP8", "Falklands"),
        ("VP9", "Bermuda"), ("VQ9", "Chagos"), ("VR", "Hong Kong"), ("VU", "India"),
        ("VU4", "Andaman"), ("VU7", "Lakshadweep"),

        ("XE", "Mexico"), ("XF4", "Revillagigedo"), ("XT", "Burkina Faso"), ("XU", "Cambodia"),
        ("XW", "Laos"), ("XX9", "Macao"), ("XZ", "Myanmar"),

        ("YA", "Afghanistan"), ("YB", "Indonesia"), ("YI", "Iraq"), ("YJ", "Vanuatu"),
        ("YK", "Syria"), ("YL", "Latvia"), ("YN", "Nicaragua"), ("YO", "Romania"),
        ("YS", "El Salvador"), ("YU", "Serbia"), ("YV", "Venezuela"), ("YY", "Venezuela"),

        ("Z2", "Zimbabwe"), ("Z3", "N Macedonia"), ("ZA", "Albania"), ("ZB2", "Gibraltar"),
        ("ZC4", "UK Base Cyprus"), ("ZD7", "St Helena"), ("ZD8", "Ascension"), ("ZD9", "Tristan"),
        ("ZF", "Cayman"), ("ZK3", "Tokelau"), ("ZL", "New Zealand"), ("ZL7", "Chatham"),
        ("ZL8", "Kermadec"), ("ZL9", "Auckland/Camp"), ("ZP", "Paraguay"), ("ZS", "South Africa"),
        ("ZS8", "Marion Is"), ("Z8", "S Sudan"),
    ];

    /// <summary>
    /// ITU call sign blocks not listed above (e.g. Colombia 5J/5K/HJ, Taiwan BM–BQ, Canada CF–CK).
    /// Entries in <see cref="Table"/> win when the same prefix appears in both.
    /// </summary>
    static readonly (string Prefix, string Country)[] ItuBlocks =
    [
        .. Block("Panama", "3E 3F H3 H8 H9 HO"),
        .. Block("China", "3H 3J 3K 3L 3M 3N 3O 3P 3Q 3R 3S 3T 3U XS"),
        .. Block("Mexico", "4A 4B 4C 6D 6E 6F 6G 6H 6I 6J XA XB XC XD XF XG XH XI"),
        .. Block("Philippines", "4D 4E 4F 4G 4H 4I DV DW DX DY DZ"),
        .. Block("Azerbaijan", "4J"),
        .. Block("Venezuela", "4M YW YX"),
        .. Block("Sri Lanka", "4P 4Q 4R"),
        .. Block("Peru", "4T OB OC"),
        .. Block("Haiti", "4V"),
        .. Block("Timor-Leste", "4W"),
        .. Block("Morocco", "5C 5D 5E 5F 5G"),
        .. Block("Tanzania", "5I"),
        .. Block("Colombia", "5J 5K HJ"),
        .. Block("Liberia", "5L 5M 6Z A8 D5"),
        .. Block("Nigeria", "5O"),
        .. Block("Denmark", "5P 5Q OU OV OW"),
        .. Block("Madagascar", "5S 6X"),
        .. Block("Kenya", "5Y"),
        .. Block("Egypt", "6A 6B"),
        .. Block("Syria", "6C"),
        .. Block("S Korea", "6K 6L 6M 6N D7 D8 D9 DS DT"),
        .. Block("Somalia", "6O T5"),
        .. Block("Pakistan", "6P 6Q 6R 6S AQ AR AS"),
        .. Block("Sudan", "6T 6U"),
        .. Block("Senegal", "6V"),
        .. Block("Indonesia", "7A 7B 7C 7D 7E 7F 7G 7H 7I 8A 8B 8C 8D 8E 8F 8G 8H 8I JZ PL PM PN PO YC YD YE YF YG YH"),
        .. Block("Japan", "7J 7K 7L 7M 7N 8J 8K 8L 8M 8N"),
        .. Block("Algeria", "7R 7T 7U 7V 7W 7Y"),
        .. Block("Sweden", "7S 8S SA SB SC SD SE SF SG SH SI SJ SK SL"),
        .. Block("Saudi Arabia", "7Z 8Z"),
        .. Block("Botswana", "8O"),
        .. Block("India", "8T 8U 8V 8W 8X 8Y AT AU AV AW VT VV VW"),
        .. Block("Iran", "9B 9C 9D EQ"),
        .. Block("Ethiopia", "9E 9F"),
        .. Block("Zambia", "9I"),
        .. Block("DR Congo", "9O 9P 9R 9S 9T"),
        .. Block("Burundi", "9U"),
        .. Block("Malaysia", "9W"),
        .. Block("Rwanda", "9X"),
        .. Block("Trinidad", "9Z"),
        .. Block("Spain", "AM AN AO EB EC ED EE EF EG EH"),
        .. Block("Australia", "AX VH VI VJ VL VM VN VZ"),
        .. Block("Argentina", "AY AZ L2 L3 L4 L5 L6 L7 L8 L9"),
        .. Block("Taiwan", "BM BN BO BP BQ BU BX"),
        .. Block("Cyprus", "C4 H2 P3"),
        .. Block("Mozambique", "C8"),
        .. Block("Chile", "CA CB CC CD XQ XR"),
        .. Block("Canada", "CF CG CH CI CJ CK CY CZ VB VC VD VF VG VX XJ XK XL XM XN XO"),
        .. Block("Cuba", "CL CM T4"),
        .. Block("Uruguay", "CV CW"),
        .. Block("Angola", "D3"),
        .. Block("Germany", "DA DB DC DD DE DF DG DH DI DJ DK"),
        .. Block("Ireland", "EJ"),
        .. Block("Ukraine", "EM EN EO"),
        .. Block("Belarus", "EV EW"),
        .. Block("Nicaragua", "H6 H7 HT"),
        .. Block("Ecuador", "HD"),
        .. Block("Switzerland", "HE"),
        .. Block("Poland", "HF SN SO SQ SR"),
        .. Block("Hungary", "HG"),
        .. Block("N Korea", "HM P5 P6 P7 P8 P9"),
        .. Block("Iraq", "HN"),
        .. Block("Honduras", "HQ"),
        .. Block("El Salvador", "HU"),
        .. Block("France", "HW HX HY TH TM TO TP TQ TV TW TX"),
        .. Block("Greece", "J4 SW SX SZ"),
        .. Block("Mongolia", "JU JV"),
        .. Block("Finland", "OF OG OI OJ"),
        .. Block("Belgium", "OO OP OQ OR OS OT"),
        .. Block("Curacao", "PJ"),
        .. Block("Bangladesh", "S3"),
        .. Block("Singapore", "S6"),
        .. Block("South Africa", "S8 ZR ZT ZU"),
        .. Block("Afghanistan", "T6"),
        .. Block("Bosnia", "T9"),
        .. Block("Turkey", "TB TC YM"),
        .. Block("Guatemala", "TD"),
        .. Block("Costa Rica", "TE"),
        .. Block("Tunisia", "TS"),
        .. Block("Uzbekistan", "UJ UM"),
        .. Block("Kazakhstan", "UO UP UQ"),
        .. Block("Hong Kong", "VS"),
        .. Block("Greenland", "XP"),
        .. Block("Vietnam", "XV"),
        .. Block("Macao", "XX"),
        .. Block("Myanmar", "XY"),
        .. Block("Romania", "YP YQ YR"),
        .. Block("Serbia", "YT YZ"),
        .. Block("Kosovo", "Z6"),
        .. Block("New Zealand", "ZK ZM"),
        .. Block("Brazil", "ZV ZW ZX ZY ZZ"),
    ];

    static IEnumerable<(string Prefix, string Country)> Block(string country, string prefixes) =>
        prefixes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(p => (p, country));

    static readonly (string Prefix, string Country)[] Sorted =
        Table.Concat(ItuBlocks)
            .OrderByDescending(t => t.Prefix.Length)
            .ThenBy(t => t.Prefix, StringComparer.Ordinal)
            .ToArray();

    public static string? FromCall(string? callsign)
    {
        string call = Normalize(callsign);
        if (call.Length == 0) return null;

        foreach (var (prefix, country) in Sorted)
        {
            // Only KG4 + two letters is Guantanamo; KG4ABC and KG4A are ordinary US calls.
            if (prefix == "KG4" && call.Length != 5)
                continue;
            if (call.StartsWith(prefix, StringComparison.Ordinal))
                return country;
        }
        return null;
    }

    static string Normalize(string? callsign)
    {
        if (string.IsNullOrWhiteSpace(callsign)) return "";
        string c = callsign.Trim().ToUpperInvariant();

        // Portable /P /M etc.
        int slash = c.IndexOf('/');
        if (slash <= 0) return c;

        string left = c[..slash];
        string right = c[(slash + 1)..];
        if (right is "P" or "M" or "QRP" or "A" or "B" or "MM" or "AM" or "LH")
            return left;

        // Prefix portable: W4/G4ABC → prefer home call (right) when it looks like a call
        if (right.Length >= 3 && left.Length <= 4)
            return right;
        return left;
    }
}
