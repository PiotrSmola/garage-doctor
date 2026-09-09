# Data manifest

Dane NIE sa przechowywane w tym repozytorium. Ten plik dokumentuje zbior,
na ktorym projekt byl budowany, zeby wyniki byly odtwarzalne.

Zrodlo:   NHTSA Office of Defects Investigation
          https://static.nhtsa.gov/odi/ffdd/
Licencja: us-pd (domena publiczna, bez warunkow uzycia)

Pobrano: 2026-08-21

## FLAT_CMPL.txt - zgloszenia usterek

Rozmiar:       1518.2 MB
Wierszy:       2237179   (referencja: 2235299)
Pol w wierszu: 51 (TAB-delimited, bez naglowka)
Konce linii:   LF

## FLAT_RCL_POST_2010.txt - akcje serwisowe od 2010

Wierszy:       244499   (referencja: 244499)
Pol w wierszu: 29 (TAB-delimited, bez naglowka)
Konce linii:   CRLF

## FLAT_RCL_PRE_2010.txt - akcje serwisowe 1967-2009

Wierszy:       81715   (referencja: 81715, w tym 5 pustych)

Pliki sa aktualizowane codziennie, wiec liczby beda rosly.

## Jak odtworzyc

    make data

## Dlaczego danych nie ma w repo

Pole CDESCR zawiera swobodny tekst pisany przez wlascicieli pojazdow.
Mimo ze zbior jest w domenie publicznej, narracje regularnie zawieraja
nazwiska, adresy i numery telefonu wpisane przez zglaszajacych.
Redystrybucja tych rekordow oznaczalaby publikowanie cudzych danych osobowych.

Testy korzystaja z syntetycznych fixtures w tests/fixtures/ - pisanych recznie.
