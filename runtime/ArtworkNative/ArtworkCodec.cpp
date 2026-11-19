#include <stdint.h>
#include <mutex>
#include <thread>
#include <vector>
#include <atomic>
#include "vendor/bc7enc.h"
static std::once_flag init;
extern "C" __declspec(dllexport) int __cdecl OTBA_EncodeBGRA(const uint8_t* pixels,uint32_t width,uint32_t height,uint8_t* output,uint32_t outputBytes) noexcept {
    if(!pixels||!output||!width||!height||width>8192||height>8192||(width&3)||(height&3))return 1;
    uint64_t needed=uint64_t(width/4)*(height/4)*16;
    if(needed!=outputBytes||uint64_t(width)*height>16777216)return 2;
    try{
        std::call_once(init,[]{bc7enc_compress_block_init();});
        std::atomic<uint32_t> row{0};
        auto work=[&]{
            bc7enc_compress_block_params p;bc7enc_compress_block_params_init(&p);p.m_max_partitions=8;p.m_uber_level=0;
            color_rgba block[16];uint32_t y;
            while((y=row.fetch_add(1))<height/4){
                for(uint32_t x=0;x<width/4;x++){
                    for(uint32_t by=0;by<4;by++)for(uint32_t bx=0;bx<4;bx++){
                        const auto* src=pixels+((y*4+by)*width+x*4+bx)*4;
                        auto& dst=block[by*4+bx];dst.m_c[0]=src[2];dst.m_c[1]=src[1];dst.m_c[2]=src[0];dst.m_c[3]=src[3];
                    }
                    bc7enc_compress_block(output+(uint64_t(y)*(width/4)+x)*16,block,&p);
                }
            }
        };
        uint32_t count=std::thread::hardware_concurrency();if(count<1)count=1;if(count>4)count=4;
        std::vector<std::thread> workers;workers.reserve(count);
        try{for(uint32_t i=1;i<count;i++)workers.emplace_back(work);work();}
        catch(...){for(auto& t:workers)if(t.joinable())t.join();return 3;}
        for(auto& t:workers)t.join();
        return 0;
    }catch(...){return 3;}
}
