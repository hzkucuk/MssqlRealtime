/* =============================================================================
   Sunucu Izleme — izlenecek SQL Server tarafinda yapilacaklar
   SSMS'te master veritabaninda calistirin.

   Izlenen sunucuya HICBIR SEY KURULMAZ. Yalnizca salt okunur bir hesap gerekir.

   Iki secenek var:
     A) SQL girisi        — panel baska bir makinedeyse
     B) Windows hesabi    — panel bu makinede servis olarak calisiyorsa (parola saklanmaz)

   Ihtiyacinizi karsilayan bolumu calistirin, digerini atlayin.
   ============================================================================= */


/* -----------------------------------------------------------------------------
   A) SQL GIRISI
   --------------------------------------------------------------------------- */
USE master;
GO

-- Parolayi degistirin.
CREATE LOGIN [izleme] WITH PASSWORD = N'BURAYA-GUCLU-BIR-PAROLA', CHECK_POLICY = ON;
GO

-- Zorunlu: oturumlar, calisan sorgular, blocking, islemci/bellek, bekleme istatistikleri.
GRANT VIEW SERVER STATE TO [izleme];

-- Zorunlu: nesne adlarini cozebilmek icin (veritabani listesi, kurtarma modeli vb.).
GRANT VIEW ANY DEFINITION TO [izleme];
GO

-- Istege bagli: "Son yedek" sutununun dolmasi icin msdb okuma yetkisi.
-- Verilmezse panel calisir, yalniz son yedek tarihi bos gorunur.
USE msdb;
CREATE USER [izleme] FOR LOGIN [izleme];
ALTER ROLE db_datareader ADD MEMBER [izleme];
GO


/* -----------------------------------------------------------------------------
   B) WINDOWS HESABI  (panel bu makinede servis olarak calisiyorsa)

   Servis LocalSystem ile calisiyorsa hesap adi:  NT AUTHORITY\SYSTEM
   Etki alani hesabiyla calisiyorsa:              DOMAIN\svc_izleme

   Bu yolda panelde "Windows (entegre)" secilir ve SQL parolasi HIC SAKLANMAZ.
   --------------------------------------------------------------------------- */
/*
USE master;
GO

CREATE LOGIN [DOMAIN\svc_izleme] FROM WINDOWS;
GO

GRANT VIEW SERVER STATE TO [DOMAIN\svc_izleme];
GRANT VIEW ANY DEFINITION TO [DOMAIN\svc_izleme];
GO

USE msdb;
CREATE USER [DOMAIN\svc_izleme] FOR LOGIN [DOMAIN\svc_izleme];
ALTER ROLE db_datareader ADD MEMBER [DOMAIN\svc_izleme];
GO
*/


/* -----------------------------------------------------------------------------
   OTURUM SONLANDIRMA (KILL) — ISTEGE BAGLI

   Panelde "Kes" dugmesini kullanacaksaniz gerekir. Vermezseniz urun sorunsuz
   calisir; yalnizca o dugme yetki hatasi dondurur.

   ⚠️ Bu yetki, hesabin HERHANGI bir oturumu sonlandirabilmesini saglar. Yalnizca
      izleme yapacaksaniz vermeyin.
   --------------------------------------------------------------------------- */
/*
USE master;
GO
GRANT ALTER ANY CONNECTION TO [izleme];
GO
*/


/* =============================================================================
   DOGRULAMA — bu sorgu 1 dondurmeli
   ============================================================================= */
EXECUTE AS LOGIN = N'izleme';
GO
SELECT
    CONVERT(int, HAS_PERMS_BY_NAME(NULL, NULL, 'VIEW SERVER STATE'))   AS view_server_state,
    CONVERT(int, HAS_PERMS_BY_NAME(NULL, NULL, 'VIEW ANY DEFINITION')) AS view_any_definition,
    CONVERT(int, HAS_PERMS_BY_NAME(NULL, NULL, 'ALTER ANY CONNECTION')) AS kill_yetkisi;
GO
REVERT;
GO

/*  view_server_state = 1 ve view_any_definition = 1 olmali.
    kill_yetkisi = 0 ise "Kes" dugmesi disinda her sey calisir.

    Panelde sunucu eklerken "Baglantiyi sina" dugmesi bu izinleri zaten kontrol eder
    ve eksikse hangi GRANT'in gerektigini ekranda soyler.  */


/* =============================================================================
   KALDIRMA
   ============================================================================= */
/*
USE msdb;
DROP USER [izleme];
GO
USE master;
DROP LOGIN [izleme];
GO
*/
