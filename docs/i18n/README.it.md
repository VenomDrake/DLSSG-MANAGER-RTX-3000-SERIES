# DLSSG Mod Manager

**Leggi in:** [English](../../README.md) · **Italiano** · [简体中文](README.zh-Hans.md) · [हिन्दी](README.hi.md) · [Español](README.es.md) · [Français](README.fr.md) · [العربية](README.ar.md) · [বাংলা](README.bn.md) · [Português](README.pt.md) · [Русский](README.ru.md) · [اردو](README.ur.md)

Un'app desktop per Windows che affianca [DLSSG for SM86](https://github.com/sdli1995/dlssg_for_sm86): controlla il PC e il gioco, installa un pacchetto verificato della mod e conserva una cronologia delle modifiche da cui è possibile ripristinare lo stato precedente.

## Crediti

- **Mod originale:** [DLSSG for SM86](https://github.com/sdli1995/dlssg_for_sm86), di [sdli1995](https://github.com/sdli1995) e dei collaboratori del progetto.
- **Adattamento RTX 20 / SM75:** [Coldwood1026](https://github.com/Coldwood1026), citato nelle [note del progetto originale](https://github.com/sdli1995/dlssg_for_sm86/blob/main/THIRD_PARTY_NOTICES.txt).
- **Questo gestore:** un'interfaccia indipendente per l'installazione e la diagnostica. Il merito della mod originale resta ai suoi autori; il gestore non si attribuisce il loro lavoro e non dichiara di essere approvato ufficialmente.

![Anteprima dell'applicazione](../screenshot.png)

## Cosa fa

- Riunisce controlli del PC, scelta del gioco e impostazioni della mod in un’interfaccia scura rinnovata, mantenendo gli accenti verde menta originali.
- All'avvio verifica l'ultimo **pacchetto completo della release ufficiale**, prima di analizzare hardware e gioco. Scarica automaticamente un nuovo pacchetto e segnala quando la cache verificata è già aggiornata.
- Confronta ID della release, ID del file e SHA-256, rilevando anche file sostituiti senza cambiare il numero di versione. Lo stato offline viene distinto da “ultima versione”.
- Controlla architettura e driver NVIDIA, Windows x64 e configurazione della pianificazione GPU. La pianificazione viene confermata come attiva soltanto quando lo indica il log del runtime del gioco.
- Accetta una cartella di gioco o l'EXE di rendering e individua i giochi installati nelle librerie Steam. Trova gli eseguibili di rendering Unreal e permette di risolvere le selezioni ambigue.
- Esamina architettura x64, generazione del runtime DLSS-G nativo, indizi di DirectX 12, indicatori noti di anti-cheat, loader esistenti, processi del gioco e spazio disponibile. La presenza di file non certifica che il gioco funzionerà.
- Mostra motore, moltiplicatore, ottimizzazione, preset, override SR, impostazioni XeSS per flusso/HUD, loader e opzioni dei log. **Le impostazioni consigliate sono DLSS, 2×, ottimizzazione con risultato esatto e override SR disattivato (Off).**
- Installa o aggiorna soltanto dopo la revisione e la conferma. Prepara i file verificati, salva lo stato precedente, disattiva i vecchi loader prima di modificare i file del runtime e attiva il nuovo loader per ultimo.
- Offre disattivazione e ripristino, protegge i file modificati fuori dal gestore e registra un giornale di recupero per le operazioni interrotte. Conserva i backup, compresi gli eventuali file aggiuntivi spostati.
- Legge l'ultima sessione di gioco registrata. Per confermare la generazione effettiva dei fotogrammi richiede la creazione riuscita della funzione NGX 11 e la sua prima elaborazione.
- Esporta un report diagnostico locale con i percorsi dell'utente e del gioco oscurati. Non richiede account e non raccoglie telemetria.

## Download e utilizzo

Scarica l'[anteprima pubblica v0.2.1](https://github.com/VenomDrake/DLSSG-MANAGER-RTX-3000-SERIES/releases/tag/v0.2.1): [ZIP portatile](https://github.com/VenomDrake/DLSSG-MANAGER-RTX-3000-SERIES/releases/download/v0.2.1/DLSSGModManager-Portable-win-x64.zip) · [installer Windows](https://github.com/VenomDrake/DLSSG-MANAGER-RTX-3000-SERIES/releases/download/v0.2.1/DLSSGModManager-Setup.msi). Consulta [tutte le release](https://github.com/VenomDrake/DLSSG-MANAGER-RTX-3000-SERIES/releases) per le versioni successive.

Usa `DLSSGModManager.exe` dal pacchetto portatile oppure esegui `DLSSGModManager-Setup.msi`, che installa il programma per l'utente corrente. I binari pubblicati non richiedono l'installazione separata di .NET, Python o CUDA Toolkit.

> **Giochi online: rischio di ban**
>
> L’uso di questa mod modifica i file del gioco. Consigliamo vivamente di usarla soltanto nei giochi per giocatore singolo offline. I sistemi anti-cheat o le regole del gioco potrebbero considerarla una modifica non autorizzata e comportare un ban. Non viene fornito alcun aggiramento o protezione contro i ban.

1. Apri il gestore e lascia che controlli/scarichi il pacchetto ufficiale della mod.
2. Scegli un gioco Steam, una cartella di gioco o il suo EXE di rendering.
3. Esamina tutti i controlli. Un anti-cheat rilevato, un runtime non supportato, un loader sconosciuto in conflitto o il gioco aperto impediscono l'installazione.
4. Esamina le opzioni e inizia con **2×**. Seleziona **Installa mod** o **Aggiorna mod** e conferma la destinazione.
5. Avvia il gioco e attiva **DLSS Frame Generation** nelle impostazioni grafiche. Alcuni giochi richiedono un riavvio.
6. Seleziona **Leggi log del gioco** per verificare se la mod è stata caricata, se la funzione è stata creata e se la generazione è effettivamente iniziata.

Il gestore non modifica automaticamente HAGS o i driver. Apre le impostazioni grafiche di Windows o la pagina dei driver NVIDIA e fornisce istruzioni. Riavvia Windows dopo aver abilitato HAGS.

## Aggiornare il gestore

Il gestore controlla le proprie release GitHub in modo silenzioso in background. Un piccolo avviso appare soltanto quando è disponibile una versione stabile più recente del gestore; le bozze e le versioni preliminari vengono ignorate.

Scegli **Aggiorna e riavvia** quando preferisci, dopo aver terminato l’operazione in corso. Il gestore seleziona automaticamente lo ZIP portatile o l’MSI adatto all’installazione corrente, lo scarica, ne verifica lo SHA-256 confrontandolo con il digest del file della release ufficiale GitHub, si chiude, installa l’aggiornamento e si riapre. Giochi, impostazioni e backup vengono conservati. Gli aggiornamenti successivi non richiedono di visitare GitHub o sostituire manualmente i file.

**Il passaggio dalla 0.1.0 alla 0.2.0 richiede un solo aggiornamento manuale**, perché la 0.1.0 non contiene il sistema di aggiornamento. Chiudi la 0.1.0, poi esegui il nuovo MSI oppure estrai l’intero nuovo pacchetto portatile nella cartella dell’applicazione portatile, usando lo stesso tipo di pacchetto precedente. Dalla 0.2.0, gli aggiornamenti successivi del gestore possono essere installati nell’app.

Gli aggiornamenti del gestore e quelli della mod sono separati. Aggiornare il gestore non installa una nuova mod nei giochi; la procedura d’installazione originale verificata resta la **0.4.1**.

## Ambito supportato e limitazioni

La versione 0.2.0 è destinata a Windows 10/11 x64 e alle GPU RTX 20/30 supportate dal progetto originale. La procedura d'installazione attualmente verificata riguarda la versione **0.4.1** della mod, con driver R580 o successivo e runtime 310.9.16. Il gestore scarica anche le release ufficiali future, ma una procedura sconosciuta viene contrassegnata come non supportata finché i controlli del gestore non vengono aggiornati; un nuovo numero di versione non dimostra che i requisiti siano compatibili.

Il gioco deve includere DLSS Frame Generation nativo ed essere eseguito con DirectX 12. La procedura attuale supporta i runtime 310.x; le vecchie generazioni di runtime vengono rifiutate. I moltiplicatori più alti dipendono dall'integrazione nel gioco. XeSS è limitato a 4×; DLSS 6× richiede un'integrazione che lo supporti.

Gli indicatori noti di anti-cheat bloccano l'installazione. La loro assenza non dimostra che un gioco sia privo di anti-cheat. Non usare mod iniettate nei giochi online protetti da anti-cheat.

Le installazioni manuali esistenti che corrispondono alla release verificata corrente possono essere gestite dopo il controllo degli hash delle DLL. Una mod manuale più vecchia o sconosciuta, senza un record del gestore, viene trattata come conflitto anziché sovrascritta. Le installazioni effettuate successivamente dal gestore possono essere aggiornate e ripristinate usando inventario e backup.

La prima verifica nel log è un test iniziale, non una garanzia di stabilità durante lunghe sessioni o di raddoppio degli FPS renderizzati. I fotogrammi generati visualizzati e la latenza dei comandi sono misure diverse.

Il gestore e l'MSI non sono firmati in questa build. Non viene importato alcun certificato di firma. Il pacchetto originale 0.4.1 usa un certificato di test autofirmato il cui nome differisce da quello nel suo README; la provenienza del pacchetto viene verificata tramite il digest del file su GitHub, non tramite l'attendibilità del publisher in Windows.

## Lingue

L'inglese è la lingua di origine; sono disponibili anche italiano, cinese semplificato, hindi, spagnolo, francese, arabo, bengalese, portoghese, russo e urdu. L'app segue una lingua dell'interfaccia Windows supportata e permette di cambiarla. Arabo e urdu usano una disposizione da destra a sinistra. Tutti gli 11 file delle risorse hanno le stesse chiavi; le revisioni delle traduzioni da parte della comunità sono benvenute. I valori di configurazione avanzati e i dettagli delle eccezioni diagnostiche possono mantenere i nomi tecnici originali.

## Dati locali e recupero

- Cache verificata dei pacchetti e preferenza della lingua: `%LOCALAPPDATA%\DLSSGModManager`.
- Inventario del gioco: `.dlssg-manager.json` accanto all'EXE di rendering.
- Backup: `.dlssg-manager-backups` accanto allo stesso EXE. Conserva questa cartella per mantenere la cronologia dei ripristini.
- Il file `.dlssg-manager.transaction.json` indica un'operazione interrotta; la successiva installazione, il ripristino o la disattivazione la recuperano prima di procedere e richiedono che il gioco sia chiuso.

Usa ripetutamente **Ripristina stato precedente** per tornare indietro attraverso gli aggiornamenti gestiti. Il ripristino della prima installazione gestita restituisce lo stato originale, compresa un'eventuale installazione manuale adottata. La disattivazione sposta semplicemente il loader gestito fuori dal suo nome DLL attivo. Il gestore non elimina file utente sconosciuti e non annulla silenziosamente modifiche esterne ai file gestiti.

La disinstallazione del gestore tramite Windows rimuove l'applicazione, non le mod installate nei giochi. Ripristina i giochi interessati prima di disinstallarlo se vuoi rimuovere anche quelle mod. I backup dei giochi e la cache scaricata vengono conservati.

## Compilazione e test

Installa .NET 10 SDK su Windows x64, quindi esegui:

```powershell
.\scripts\test.ps1
.\scripts\build.ps1
```

Lo script di compilazione pubblica un'applicazione autonoma in un singolo file, uno ZIP portatile, un MSI e i checksum SHA-256 in `artifacts`. Se necessario, WiX 4.0.6 viene installato come strumento di compilazione in `tools`. L'output non contiene DLL scaricate della mod.

I test usano pacchetti sintetici e una DLL di prova x64 innocua con metadati di versione. Non richiedono giochi, hardware NVIDIA o una connessione di rete. È disponibile un test facoltativo della release online con `dotnet run --project tests/ReleaseTests -- --live`.

Per un'anteprima dell'interfaccia in sola lettura su un gioco locale:

```powershell
dotnet run --project src/DlssgManager.Desktop -- --read-only --game 'D:\Games\Example'
```

`--data-dir <path>` isola la cache e le preferenze del gestore per i test. `--read-only` disabilita le modifiche ai file del gioco.

## Pubblicare un fork

Crea una repository GitHub pubblica e carica questo albero dei sorgenti. Il workflow Windows incluso compila e testa le pull request e i push sul ramo principale. Un tag come `v0.2.1` pubblica anche i binari come release GitHub usando il token Actions della repository. Nessun token viene salvato nei sorgenti o richiesto agli utenti finali.

Consulta [CONTRIBUTING.md](../../CONTRIBUTING.md), [SECURITY.md](../../SECURITY.md) e [THIRD_PARTY_NOTICES.md](../../THIRD_PARTY_NOTICES.md). Questo gestore indipendente è distribuito con licenza MIT; il materiale della mod originale mantiene le proprie condizioni.
