package uk.co.skyeye.companion.nativebridge;

public final class DecodedFrameLedgerChecks {
    public static void main(String[] args) {
        DecodedFrameLedger ledger=new DecodedFrameLedger();ledger.reset(1);
        ledger.frame(1,1000,100,1920,1080);ledger.frame(1,34000,200,1920,1080);
        check(ledger.match(1,100).presentationUs==1000,"Exact surface identity survives a newer released frame");
        check(ledger.match(1,199)==null,"No nearest-release approximation");
        check(ledger.match(2,100)==null,"Unrelated generation rejected");
        ledger.frame(1,35000,100,1920,1080);
        check(ledger.match(1,100)==null,"Colliding release timestamps stay ambiguous");
        ledger.reset(2);check(ledger.match(2,200)==null,"Reconnect cannot retain old metadata");
        ledger.frame(1,1000,300,1920,1080);ledger.frame(2,-1,400,1920,1080);ledger.frame(2,1,500,5000,1080);
        check(ledger.size()==0,"Late old callback and unsupported metadata rejected");
        for(int i=1;i<=100;i++)ledger.frame(2,i,i,640,360);
        check(ledger.size()==64&&ledger.match(2,1)==null&&ledger.match(2,100).frameId==100,"Metadata memory is bounded");
        ledger.reset(3);ledger.frame(3,0,1000,640,360);
        check(ledger.match(3,1000).frameId==1,"Receipt IDs restart only in a new scope");
        System.out.println("PASS native frame ledger: exact surface match, no newest/nearest selection, generation/collision/metadata fences and bounded retention");
    }
    static void check(boolean ok,String message){if(!ok)throw new AssertionError(message);}
}
