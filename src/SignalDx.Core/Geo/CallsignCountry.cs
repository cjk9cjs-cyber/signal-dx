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
        ("UX", "Ukraine"), ("UZ", "Uzbekistan"),

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

    static readonly (string Prefix, string Country)[] Sorted =
        Table.OrderByDescending(t => t.Prefix.Length)
            .ThenBy(t => t.Prefix, StringComparer.Ordinal)
            .ToArray();

    public static string? FromCall(string? callsign)
    {
        string call = Normalize(callsign);
        if (call.Length == 0) return null;

        foreach (var (prefix, country) in Sorted)
        {
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
